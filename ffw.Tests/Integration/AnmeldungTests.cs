using System.Net;

namespace ffw.Tests.Integration;

/// <summary>Anmeldung über das echte Formular: richtige und falsche Zugangsdaten, Sperre, Weiterleitung.</summary>
[Collection(AnwendungCollection.Name)]
public class AnmeldungTests(AnwendungFixture app)
{
    [Fact]
    public async Task Richtige_Zugangsdaten_melden_an_und_leiten_zu_den_Fotos()
    {
        var email = await app.BenutzerAnlegenAsync(null);
        var client = app.NeuerClient();

        var antwort = await app.AnmeldenAsync(client, email, AnwendungFixture.Passwort);

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Equal("/fotos", antwort.Headers.Location!.OriginalString);

        // Das Login-Cookie wirkt: das Konto ist jetzt ohne weitere Anmeldung erreichbar.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/konto")).StatusCode);
    }

    [Fact]
    public async Task Der_Admin_aus_den_Einstellungen_kann_sich_anmelden()
    {
        var antwort = await app.AnmeldenAsync(app.NeuerClient(), AnwendungFixture.AdminEmail, AnwendungFixture.Passwort);

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
    }

    [Fact]
    public async Task Falsches_Passwort_wird_abgelehnt()
    {
        var email = await app.BenutzerAnlegenAsync(null);
        var client = app.NeuerClient();

        var antwort = await app.AnmeldenAsync(client, email, "Falsches-Passwort-1");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", await antwort.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/konto")).StatusCode);
    }

    [Fact]
    public async Task Unbekannte_Adresse_bekommt_dieselbe_Fehlermeldung_wie_ein_falsches_Passwort()
    {
        var antwort = await app.AnmeldenAsync(app.NeuerClient(), "gibt-es-nicht@ffw-test.example", AnwendungFixture.Passwort);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Contains("E-Mail-Adresse oder Passwort ist falsch.", await antwort.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Nach_fuenf_Fehlversuchen_ist_das_Konto_gesperrt_auch_mit_richtigem_Passwort()
    {
        var email = await app.BenutzerAnlegenAsync(null);
        var client = app.NeuerClient();

        for (var versuch = 0; versuch < 5; versuch++)
        {
            await app.AnmeldenAsync(client, email, "Falsches-Passwort-1");
        }

        var antwort = await app.AnmeldenAsync(client, email, AnwendungFixture.Passwort);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Contains("Das Konto ist gesperrt", await antwort.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rueckkehr_auf_eine_fremde_Seite_wird_ignoriert()
    {
        var email = await app.BenutzerAnlegenAsync(null);

        var antwort = await app.AnmeldenAsync(app.NeuerClient(), email, AnwendungFixture.Passwort, "https://boese.example/");

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Equal("/fotos", antwort.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Rueckkehr_auf_eine_eigene_Seite_funktioniert()
    {
        var email = await app.BenutzerAnlegenAsync(null);

        var antwort = await app.AnmeldenAsync(app.NeuerClient(), email, AnwendungFixture.Passwort, "/konto");

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Equal("/konto", antwort.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Formular_ohne_Antiforgery_Token_wird_abgelehnt()
    {
        var email = await app.BenutzerAnlegenAsync(null);

        var antwort = await app.NeuerClient().PostAsync("/anmelden", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = AnwendungFixture.Passwort,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }
}
