using System.Net;
using System.Text;
using ffw.Data;

namespace ffw.Tests.Integration;

/// <summary>Wer darf welche Seite und welche Datei sehen?</summary>
[Collection(AnwendungCollection.Name)]
public class ZugriffTests(AnwendungFixture app)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< >>\nendobj\n");
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00];

    [Theory]
    [InlineData("/")]
    [InlineData("/anmelden")]
    [InlineData("/kostenuhr")]
    public async Task Oeffentliche_Seiten_sind_ohne_Anmeldung_erreichbar(string pfad)
    {
        var antwort = await app.NeuerClient().GetAsync(pfad);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    [Theory]
    [InlineData("/fotos")]
    [InlineData("/dokumente")]
    [InlineData("/benutzer")]
    [InlineData("/konto")]
    public async Task Geschuetzte_Seiten_leiten_Besucher_zur_Anmeldung(string pfad)
    {
        var antwort = await app.NeuerClient().GetAsync(pfad);

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Contains("/anmelden", antwort.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/fotos/bild/")]
    [InlineData("/fotos/vorschau/")]
    [InlineData("/dokumente/datei/")]
    public async Task Dateien_sind_ohne_Anmeldung_nicht_abrufbar(string pfad)
    {
        var antwort = await app.NeuerClient().GetAsync(pfad + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Contains("/anmelden", antwort.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(Rollen.Mitglied, "/fotos")]
    [InlineData(Rollen.Mitglied, "/dokumente")]
    [InlineData(Rollen.Mitglied, "/konto")]
    [InlineData(Rollen.Redakteur, "/fotos")]
    [InlineData(Rollen.Redakteur, "/dokumente")]
    [InlineData(Rollen.Admin, "/fotos")]
    [InlineData(Rollen.Admin, "/benutzer")]
    [InlineData(null, "/konto")]
    public async Task Erlaubte_Seiten_werden_angezeigt(string? rolle, string pfad)
    {
        var client = await app.AngemeldeterClientAsync(await app.BenutzerAnlegenAsync(rolle));

        var antwort = await client.GetAsync(pfad);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    [Theory]
    [InlineData(Rollen.Mitglied, "/benutzer")]
    [InlineData(Rollen.Redakteur, "/benutzer")]
    [InlineData(null, "/fotos")]
    [InlineData(null, "/dokumente")]
    [InlineData(null, "/benutzer")]
    public async Task Fehlende_Rechte_fuehren_zur_Seite_Kein_Zugriff(string? rolle, string pfad)
    {
        var client = await app.AngemeldeterClientAsync(await app.BenutzerAnlegenAsync(rolle));

        var antwort = await client.GetAsync(pfad);

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Contains("/kein-zugriff", antwort.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Benutzer_ohne_Gruppe_sieht_keine_Fotodatei()
    {
        var client = await app.AngemeldeterClientAsync(await app.BenutzerAnlegenAsync(null));

        var antwort = await client.GetAsync("/fotos/bild/" + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Contains("/kein-zugriff", antwort.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/fotos/bild/")]
    [InlineData("/fotos/vorschau/")]
    [InlineData("/dokumente/datei/")]
    public async Task Unbekannte_Datei_liefert_404(string pfad)
    {
        var client = await app.AngemeldeterClientAsync(await app.BenutzerAnlegenAsync(Rollen.Mitglied));

        var antwort = await client.GetAsync(pfad + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task PDF_wird_im_Browser_angezeigt()
    {
        var id = await app.DokumentAnlegenAsync("Satzung.pdf", Pdf);
        var client = await app.AngemeldeterClientAsync(await app.BenutzerAnlegenAsync(Rollen.Mitglied));

        var antwort = await client.GetAsync("/dokumente/datei/" + id);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("application/pdf", antwort.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", antwort.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("nosniff", antwort.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(Pdf, await antwort.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Word_Dokument_wird_mit_dem_urspruenglichen_Namen_heruntergeladen()
    {
        var id = await app.DokumentAnlegenAsync("Antrag.docx", Zip);
        var client = await app.AngemeldeterClientAsync(await app.BenutzerAnlegenAsync(Rollen.Mitglied));

        var antwort = await client.GetAsync("/dokumente/datei/" + id);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var disposition = antwort.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition?.DispositionType);
        Assert.Equal("Antrag.docx", (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"'));
    }

    [Fact]
    public async Task Datei_mit_nicht_erlaubter_Endung_wird_nie_ausgeliefert()
    {
        // Selbst wenn so ein Eintrag in die Datenbank käme, liefert der Download ihn nicht aus.
        var id = await app.DokumentAnlegenAsync("virus.exe", Pdf);
        var client = await app.AngemeldeterClientAsync(await app.BenutzerAnlegenAsync(Rollen.Mitglied));

        var antwort = await client.GetAsync("/dokumente/datei/" + id);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }
}
