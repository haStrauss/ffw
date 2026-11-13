using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ffw.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ffw.Tests.Integration;

/// <summary>Die Bestenliste des Löschspiels über die echten Schnittstellen und die Datenbank. Ohne Anmeldung, wie im Spiel.</summary>
[Collection(AnwendungCollection.Name)]
public class LoeschspielBestenlisteApiTests(AnwendungFixture app)
{
    /// <summary>Eine Marke, die so tut, als wäre der Einsatz schon vor einiger Zeit gestartet worden.</summary>
    private string MarkeVor(TimeSpan vergangen)
    {
        using var scope = app.Anwendung.Services.CreateScope();
        var liste = scope.ServiceProvider.GetRequiredService<LoeschspielBestenliste>();
        return liste.NeueSpielmarke(DateTimeOffset.UtcNow - vergangen);
    }

    private static Task<HttpResponseMessage> Eintragen(HttpClient client, string? marke, string? name, int punkte, int sekunden)
        => client.PostAsJsonAsync("/loeschspiel/api/ergebnis", new { marke, name, punkte, sekunden });

    [Fact]
    public async Task Bestenliste_ist_ohne_Anmeldung_abrufbar_und_wird_nicht_zwischengespeichert()
    {
        var antwort = await app.NeuerClient().GetAsync("/loeschspiel/api/bestenliste");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Contains("no-store", antwort.Headers.CacheControl?.ToString());
        var json = await antwort.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, json.GetProperty("eintraege").ValueKind);
        Assert.True(json.GetProperty("eintraege").GetArrayLength() <= LoeschspielBestenliste.Plaetze);
    }

    [Fact]
    public async Task Start_liefert_eine_Spielmarke()
    {
        var antwort = await app.NeuerClient().PostAsync("/loeschspiel/api/start", null);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await antwort.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("marke").GetString()));
    }

    [Fact]
    public async Task Platz_ohne_Punkte_gibt_keinen_Platz()
    {
        var antwort = await app.NeuerClient().GetAsync("/loeschspiel/api/platz?punkte=0");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await antwort.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("platz").ValueKind);
    }

    [Fact]
    public async Task Ein_guter_Einsatz_kommt_mit_Namen_auf_Platz_1_und_laesst_sich_nur_einmal_eintragen()
    {
        var client = app.NeuerClient();
        var marke = MarkeVor(TimeSpan.FromMinutes(7));

        // Höher als alles, was andere Tests eintragen, aber noch plausibel für 400 Sekunden (40 + 12 * 400 = 4840).
        var platz = await client.GetFromJsonAsync<JsonElement>("/loeschspiel/api/platz?punkte=4800");
        Assert.Equal(1, platz.GetProperty("platz").GetInt32());

        var antwort = await Eintragen(client, marke, "  Test   Spieler ", 4800, 400);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var json = await antwort.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("platz").GetInt32());
        var erster = json.GetProperty("eintraege")[0];
        Assert.Equal("Test Spieler", erster.GetProperty("name").GetString());
        Assert.Equal(4800, erster.GetProperty("punkte").GetInt32());
        Assert.Equal(1, erster.GetProperty("platz").GetInt32());

        var nochmal = await Eintragen(client, marke, "Zweiter Versuch", 4800, 400);
        Assert.Equal(HttpStatusCode.Conflict, nochmal.StatusCode);

        var liste = await client.GetFromJsonAsync<JsonElement>("/loeschspiel/api/bestenliste");
        Assert.Equal("Test Spieler", liste.GetProperty("eintraege")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Ungueltiger_Name_wird_abgelehnt()
    {
        var antwort = await Eintragen(app.NeuerClient(), MarkeVor(TimeSpan.FromMinutes(5)), "<script>", 500, 300);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var json = await antwort.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("fehler").GetString()));
    }

    [Fact]
    public async Task Erfundene_Marke_wird_abgelehnt()
    {
        var antwort = await Eintragen(app.NeuerClient(), "erfunden", "Schummler", 500, 300);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    [Fact]
    public async Task Einsatz_ohne_Zeit_seit_dem_Start_wird_abgelehnt()
    {
        // Frisch gestartet, aber 300 Sekunden Einsatzdauer behauptet.
        var antwort = await Eintragen(app.NeuerClient(), MarkeVor(TimeSpan.Zero), "Zeitreisender", 500, 300);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    [Fact]
    public async Task Zu_viele_Punkte_fuer_die_Dauer_werden_abgelehnt()
    {
        var antwort = await Eintragen(app.NeuerClient(), MarkeVor(TimeSpan.FromMinutes(5)), "Zu Gut", 999999, 60);

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }
}
