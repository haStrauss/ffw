using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ffw.Data;

public static class DatenbankInitialisierer
{
    /// <summary>
    /// Legt die Tabellen an (falls die Datenbank noch leer ist), erzeugt die Rechtegruppen und bei Bedarf den ersten Benutzer
    /// aus den Einstellungen Admin:Email und Admin:Password (er wird immer der Gruppe Admin zugeordnet).
    /// </summary>
    public static async Task InitialisierenAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Der Datenbank-Container ist beim Start der Anwendung oft noch nicht bereit.
        const int maxVersuche = 15;
        for (var versuch = 1; ; versuch++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception ex) when (versuch < maxVersuche)
            {
                logger.LogWarning("Datenbank noch nicht erreichbar (Versuch {Versuch}/{Max}): {Meldung}", versuch, maxVersuche, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        // EnsureCreated legt nur dann Tabellen an, wenn die Datenbank noch gar nicht existiert. Tabellen, die später
        // dazukommen, müssen in einer bestehenden Datenbank deshalb extra (und wiederholbar) angelegt werden.
        await TabellenFuerDokumenteAnlegenAsync(db);
        await TabelleFuerLoeschspielAnlegenAsync(db);

        // Oberpunkte ohne Fotos bzw. Dokumente sollen nicht bestehen bleiben (auch nicht aus der Zeit vor dieser Regel).
        var leereFotoOberpunkte = await db.Kategorien.Where(k => !k.Fotos.Any()).ExecuteDeleteAsync();
        var leereDokumentOberpunkte = await db.DokumentKategorien.Where(k => !k.Dokumente.Any()).ExecuteDeleteAsync();
        if (leereFotoOberpunkte + leereDokumentOberpunkte > 0)
        {
            logger.LogInformation("{Anzahl} leere Oberpunkte wurden entfernt.", leereFotoOberpunkte + leereDokumentOberpunkte);
        }

        await RollenAnlegenAsync(scope.ServiceProvider, logger);
        await BestehendeBenutzerUebernehmenAsync(scope.ServiceProvider, db, logger);

        var email = config["Admin:Email"]?.Trim();
        var passwort = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(passwort))
        {
            logger.LogWarning("Admin:Email / Admin:Password sind nicht gesetzt - es wird kein Benutzer angelegt.");
            return;
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var benutzer = await userManager.FindByEmailAsync(email);
        if (benutzer is null)
        {
            benutzer = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var ergebnis = await userManager.CreateAsync(benutzer, passwort);
            if (!ergebnis.Succeeded)
            {
                logger.LogError("Benutzer {Email} konnte nicht angelegt werden: {Fehler}", email,
                    string.Join("; ", ergebnis.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Benutzer {Email} wurde angelegt.", email);
        }

        // Der Benutzer aus den Einstellungen ist immer Admin (auch wenn er aus der Zeit vor den Rechtegruppen stammt).
        // Der neue Security Stamp macht das alte Login-Cookie ungültig, damit die Rolle nach dem nächsten Anmelden wirkt.
        if (!await userManager.IsInRoleAsync(benutzer, Rollen.Admin))
        {
            var zuweisung = await userManager.AddToRoleAsync(benutzer, Rollen.Admin);
            if (zuweisung.Succeeded)
            {
                await userManager.UpdateSecurityStampAsync(benutzer);
                logger.LogInformation("Benutzer {Email} wurde der Gruppe {Rolle} zugewiesen.", email, Rollen.Admin);
            }
            else
            {
                logger.LogError("Benutzer {Email} konnte der Gruppe {Rolle} nicht zugewiesen werden: {Fehler}", email, Rollen.Admin,
                    string.Join("; ", zuweisung.Errors.Select(e => e.Description)));
            }
        }
    }

    /// <summary>
    /// Vor den Rechtegruppen durfte jeder Benutzer alles. Gibt es noch keinen Admin, werden deshalb Benutzer ohne Gruppe
    /// zu Admins, damit sich bei der Umstellung niemand aussperrt (auch wenn Admin:Email / Admin:Password nicht gesetzt sind).
    /// </summary>
    private static async Task BestehendeBenutzerUebernehmenAsync(IServiceProvider services, AppDbContext db, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        if ((await userManager.GetUsersInRoleAsync(Rollen.Admin)).Count > 0)
        {
            return;
        }

        var ohneGruppe = await db.Users
            .Where(u => !db.UserRoles.Any(ur => ur.UserId == u.Id))
            .ToListAsync();

        foreach (var benutzer in ohneGruppe)
        {
            var ergebnis = await userManager.AddToRoleAsync(benutzer, Rollen.Admin);
            if (ergebnis.Succeeded)
            {
                await userManager.UpdateSecurityStampAsync(benutzer);
                logger.LogInformation("Bestehender Benutzer {Email} wurde der Gruppe {Rolle} zugewiesen.", benutzer.Email, Rollen.Admin);
            }
            else
            {
                logger.LogError("Bestehender Benutzer {Email} konnte der Gruppe {Rolle} nicht zugewiesen werden: {Fehler}",
                    benutzer.Email, Rollen.Admin, string.Join("; ", ergebnis.Errors.Select(e => e.Description)));
            }
        }
    }

    /// <summary>
    /// Legt die Rechtegruppen an und hängt ihnen ihre Rechte an. Eine Gruppe bekommt ihre Standardrechte nur, wenn sie neu
    /// ist (oder noch gar kein Recht hat), damit spätere Änderungen in der Datenbank nicht bei jedem Start überschrieben werden.
    /// Die Gruppe Admin bekommt dagegen immer alle Rechte, auch solche, die in einer neuen Version hinzugekommen sind.
    /// </summary>
    private static async Task RollenAnlegenAsync(IServiceProvider services, ILogger logger)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

        foreach (var name in Rollen.Alle)
        {
            var rolle = await roleManager.FindByNameAsync(name);
            var neu = rolle is null;
            if (rolle is null)
            {
                rolle = new IdentityRole(name);
                var ergebnis = await roleManager.CreateAsync(rolle);
                if (!ergebnis.Succeeded)
                {
                    logger.LogError("Gruppe {Rolle} konnte nicht angelegt werden: {Fehler}", name,
                        string.Join("; ", ergebnis.Errors.Select(e => e.Description)));
                    continue;
                }

                logger.LogInformation("Gruppe {Rolle} wurde angelegt.", name);
            }

            var vorhanden = (await roleManager.GetClaimsAsync(rolle))
                .Where(c => c.Type == Rechte.ClaimTyp)
                .Select(c => c.Value)
                .ToHashSet();

            if (!neu && name != Rollen.Admin && vorhanden.Count > 0)
            {
                continue;
            }

            var hinzugefuegt = false;
            foreach (var recht in Rollen.StandardRechte(name).Where(r => !vorhanden.Contains(r)))
            {
                await roleManager.AddClaimAsync(rolle, new Claim(Rechte.ClaimTyp, recht));
                hinzugefuegt = true;
            }

            // Angemeldete Mitglieder der Gruppe bekommen neue Rechte erst mit einem frischen Login-Cookie.
            if (hinzugefuegt && !neu)
            {
                foreach (var benutzer in await userManager.GetUsersInRoleAsync(name))
                {
                    await userManager.UpdateSecurityStampAsync(benutzer);
                }
            }
        }
    }

    /// <summary>
    /// Legt die Tabelle für die Bestenliste des Löschspiels an, falls sie fehlt (bestehende Datenbank aus der Zeit davor).
    /// Namen und Spalten entsprechen dem, was EnsureCreated für dasselbe Modell erzeugen würde.
    /// </summary>
    private static async Task TabelleFuerLoeschspielAnlegenAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "LoeschspielErgebnisse" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                "SpielId" uuid NOT NULL,
                "Name" character varying(20) NOT NULL,
                "Punkte" integer NOT NULL,
                "Sekunden" integer NOT NULL,
                "Datum" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_LoeschspielErgebnisse" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LoeschspielErgebnisse_SpielId" ON "LoeschspielErgebnisse" ("SpielId");
            CREATE INDEX IF NOT EXISTS "IX_LoeschspielErgebnisse_Punkte" ON "LoeschspielErgebnisse" ("Punkte");
            """);
    }

    /// <summary>
    /// Legt die Tabellen für die Dokumente an, falls sie fehlen (bestehende Datenbank aus der Zeit vor der Dokumente-Seite).
    /// Namen und Spalten entsprechen dem, was EnsureCreated für dasselbe Modell erzeugen würde.
    /// </summary>
    private static async Task TabellenFuerDokumenteAnlegenAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "DokumentKategorien" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                "Name" character varying(100) NOT NULL,
                CONSTRAINT "PK_DokumentKategorien" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_DokumentKategorien_Name" ON "DokumentKategorien" ("Name");

            CREATE TABLE IF NOT EXISTS "Dokumente" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                "KategorieId" integer NOT NULL,
                "DateiId" uuid NOT NULL,
                "Name" character varying(255) NOT NULL,
                "Groesse" bigint NOT NULL,
                "HochgeladenAm" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_Dokumente" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_Dokumente_DokumentKategorien_KategorieId" FOREIGN KEY ("KategorieId")
                    REFERENCES "DokumentKategorien" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Dokumente_DateiId" ON "Dokumente" ("DateiId");
            CREATE INDEX IF NOT EXISTS "IX_Dokumente_KategorieId" ON "Dokumente" ("KategorieId");
            """);
    }
}
