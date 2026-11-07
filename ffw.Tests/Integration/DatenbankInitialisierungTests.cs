using System.Security.Claims;
using ffw.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ffw.Tests.Integration;

/// <summary>
/// Der DatenbankInitialisierer läuft bei jedem Start der Anwendung, also bei jedem Deploy, gegen die echte Datenbank.
/// Diese Tests sichern ab, dass er Gruppen und Admin richtig anlegt und bestehende Daten nicht kaputt macht.
/// </summary>
[Collection(AnwendungCollection.Name)]
public class DatenbankInitialisierungTests(AnwendungFixture app)
{
    private static async Task<List<string>> RechteDerGruppeAsync(RoleManager<IdentityRole> rollen, string name)
    {
        var rolle = await rollen.FindByNameAsync(name);
        Assert.NotNull(rolle);
        return (await rollen.GetClaimsAsync(rolle))
            .Where(c => c.Type == Rechte.ClaimTyp)
            .Select(c => c.Value)
            .OrderBy(r => r)
            .ToList();
    }

    [Fact]
    public async Task Gruppen_haben_die_vorgesehenen_Rechte()
    {
        using var scope = app.Anwendung.Services.CreateScope();
        var rollen = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var name in Rollen.Alle)
        {
            Assert.Equal(Rollen.StandardRechte(name).OrderBy(r => r), await RechteDerGruppeAsync(rollen, name));
        }
    }

    [Fact]
    public async Task Admin_aus_den_Einstellungen_existiert_und_gehoert_zur_Gruppe_Admin()
    {
        using var scope = app.Anwendung.Services.CreateScope();
        var benutzerVerwaltung = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        var admin = await benutzerVerwaltung.FindByEmailAsync(AnwendungFixture.AdminEmail);

        Assert.NotNull(admin);
        Assert.True(await benutzerVerwaltung.IsInRoleAsync(admin, Rollen.Admin));
        Assert.True(await benutzerVerwaltung.CheckPasswordAsync(admin, AnwendungFixture.Passwort));
    }

    [Fact]
    public async Task Erneute_Initialisierung_aendert_nichts()
    {
        using var vorher = app.Anwendung.Services.CreateScope();
        var db = vorher.ServiceProvider.GetRequiredService<AppDbContext>();
        var benutzerVorher = await db.Users.CountAsync();
        var rollenVorher = await db.Roles.CountAsync();
        var claimsVorher = await db.RoleClaims.CountAsync();

        await app.InitialisierenAsync();

        using var nachher = app.Anwendung.Services.CreateScope();
        var db2 = nachher.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(benutzerVorher, await db2.Users.CountAsync());
        Assert.Equal(rollenVorher, await db2.Roles.CountAsync());
        Assert.Equal(claimsVorher, await db2.RoleClaims.CountAsync());
    }

    [Fact]
    public async Task Fehlende_Rechte_der_Gruppe_Admin_werden_beim_Start_nachgetragen()
    {
        using var scope = app.Anwendung.Services.CreateScope();
        var rollen = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var admin = await rollen.FindByNameAsync(Rollen.Admin);
        Assert.NotNull(admin);

        Assert.True((await rollen.RemoveClaimAsync(admin, new Claim(Rechte.ClaimTyp, Rechte.DokumenteBearbeiten))).Succeeded);

        await app.InitialisierenAsync();

        using var nachher = app.Anwendung.Services.CreateScope();
        var rollenNachher = nachher.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Assert.Contains(Rechte.DokumenteBearbeiten, await RechteDerGruppeAsync(rollenNachher, Rollen.Admin));
    }

    [Fact]
    public async Task Angepasste_Rechte_anderer_Gruppen_bleiben_beim_Start_erhalten()
    {
        var recht = new Claim(Rechte.ClaimTyp, Rechte.FotosBearbeiten);
        using var scope = app.Anwendung.Services.CreateScope();
        var rollen = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var redakteur = await rollen.FindByNameAsync(Rollen.Redakteur);
        Assert.NotNull(redakteur);

        Assert.True((await rollen.RemoveClaimAsync(redakteur, recht)).Succeeded);
        try
        {
            await app.InitialisierenAsync();

            using var nachher = app.Anwendung.Services.CreateScope();
            var rollenNachher = nachher.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.DoesNotContain(Rechte.FotosBearbeiten, await RechteDerGruppeAsync(rollenNachher, Rollen.Redakteur));
        }
        finally
        {
            // Zustand für die anderen Tests wiederherstellen.
            await rollen.AddClaimAsync(redakteur, recht);
        }
    }

    [Fact]
    public async Task Oberpunkte_ohne_Inhalt_werden_beim_Start_entfernt()
    {
        var fotoName = "Leer-" + Guid.NewGuid().ToString("N");
        var dokumentName = "Leer-" + Guid.NewGuid().ToString("N");
        using (var scope = app.Anwendung.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Kategorien.Add(new FotoKategorie { Name = fotoName });
            db.DokumentKategorien.Add(new DokumentKategorie { Name = dokumentName });
            await db.SaveChangesAsync();
        }

        await app.InitialisierenAsync();

        using var nachher = app.Anwendung.Services.CreateScope();
        var db2 = nachher.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db2.Kategorien.AnyAsync(k => k.Name == fotoName));
        Assert.False(await db2.DokumentKategorien.AnyAsync(k => k.Name == dokumentName));
    }

    [Fact]
    public async Task Oberpunkte_mit_Dokumenten_bleiben_beim_Start_bestehen()
    {
        var dateiId = await app.DokumentAnlegenAsync("Bleibt.pdf", "%PDF-1.7\n"u8.ToArray());

        await app.InitialisierenAsync();

        using var scope = app.Anwendung.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Dokumente.AnyAsync(d => d.DateiId == dateiId));
    }
}
