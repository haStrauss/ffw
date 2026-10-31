using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ffw.Data;

public static class DatenbankInitialisierer
{
    /// <summary>
    /// Legt die Tabellen an (falls die Datenbank noch leer ist) und erzeugt bei Bedarf den ersten Benutzer
    /// aus den Einstellungen Admin:Email und Admin:Password.
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

        var email = config["Admin:Email"]?.Trim();
        var passwort = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(passwort))
        {
            logger.LogWarning("Admin:Email / Admin:Password sind nicht gesetzt - es wird kein Benutzer angelegt.");
            return;
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var benutzer = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var ergebnis = await userManager.CreateAsync(benutzer, passwort);
        if (ergebnis.Succeeded)
        {
            logger.LogInformation("Benutzer {Email} wurde angelegt.", email);
        }
        else
        {
            logger.LogError("Benutzer {Email} konnte nicht angelegt werden: {Fehler}", email,
                string.Join("; ", ergebnis.Errors.Select(e => e.Description)));
        }
    }
}
