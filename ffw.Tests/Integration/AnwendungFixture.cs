using System.Net;
using System.Text.RegularExpressions;
using ffw.Data;
using ffw.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace ffw.Tests.Integration;

/// <summary>
/// Startet die komplette Anwendung einmal pro Testlauf gegen eine frische PostgreSQL-Datenbank in einem Docker-Container
/// (dieselbe Version wie in docker-compose.yml). Dateien, Schlüssel und Admin-Zugang liegen in einem temporären Verzeichnis.
/// </summary>
public sealed class AnwendungFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@ffw-test.example";

    /// <summary>Erfüllt die Passwortregeln (mindestens 10 Zeichen, Groß- und Kleinbuchstaben, Ziffer, Sonderzeichen).</summary>
    public const string Passwort = "Test-Passwort-12345";

    private readonly PostgreSqlContainer _datenbank = new PostgreSqlBuilder().WithImage("postgres:16").Build();
    private readonly Dictionary<string, string?> _alteUmgebung = new();
    private string _verzeichnis = "";

    public WebApplicationFactory<Program> Anwendung { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _datenbank.StartAsync();

        _verzeichnis = Path.Combine(Path.GetTempPath(), "ffw-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_verzeichnis);

        // Umgebungsvariablen statt ConfigureAppConfiguration: Program.cs liest die Verbindungszeichenfolge,
        // bevor die Test-Konfiguration der Factory greift.
        Setzen("ConnectionStrings__DefaultConnection", _datenbank.GetConnectionString());
        Setzen("Admin__Email", AdminEmail);
        Setzen("Admin__Password", Passwort);
        Setzen("Fotos__Speicherpfad", Path.Combine(_verzeichnis, "fotos"));
        Setzen("Dokumente__Speicherpfad", Path.Combine(_verzeichnis, "dokumente"));
        Setzen("DataProtection__Pfad", Path.Combine(_verzeichnis, "keys"));

        Anwendung = new WebApplicationFactory<Program>();

        // Der erste Client startet die Anwendung, inklusive DatenbankInitialisierer.
        using var client = Anwendung.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (Anwendung is not null)
        {
            await Anwendung.DisposeAsync();
        }

        await _datenbank.DisposeAsync();

        foreach (var (name, wert) in _alteUmgebung)
        {
            Environment.SetEnvironmentVariable(name, wert);
        }

        try
        {
            Directory.Delete(_verzeichnis, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Aufräumen ist best effort.
        }
    }

    private void Setzen(string name, string wert)
    {
        _alteUmgebung[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, wert);
    }

    // --- HTTP ---

    /// <summary>Ein Browser ohne automatische Weiterleitungen, damit man die Weiterleitung selbst prüfen kann.</summary>
    public HttpClient NeuerClient()
        => Anwendung.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>Meldet über das echte Formular an (inklusive Antiforgery-Token) und gibt die Antwort des POST zurück.</summary>
    public async Task<HttpResponseMessage> AnmeldenAsync(HttpClient client, string email, string passwort, string? rueckkehrUrl = null)
    {
        var url = "/anmelden" + (rueckkehrUrl is null ? "" : "?returnUrl=" + Uri.EscapeDataString(rueckkehrUrl));

        var seite = await client.GetAsync(url);
        seite.EnsureSuccessStatusCode();
        var token = TokenAus(await seite.Content.ReadAsStringAsync());

        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = passwort,
            ["__RequestVerificationToken"] = token,
        }));
    }

    /// <summary>Ein Client, der als der angegebene Benutzer angemeldet ist.</summary>
    public async Task<HttpClient> AngemeldeterClientAsync(string email)
    {
        var client = NeuerClient();
        var antwort = await AnmeldenAsync(client, email, Passwort);
        if (antwort.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Anmeldung von {email} ist fehlgeschlagen: {(int)antwort.StatusCode}");
        }

        return client;
    }

    public static string TokenAus(string html)
    {
        var treffer = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!treffer.Success)
        {
            throw new InvalidOperationException("Im Formular wurde kein Antiforgery-Token gefunden.");
        }

        return WebUtility.HtmlDecode(treffer.Groups[1].Value);
    }

    // --- Testdaten ---

    /// <summary>Legt einen Benutzer mit dem Standard-Passwort an. Mit rolle = null gehört er zu keiner Gruppe.</summary>
    public async Task<string> BenutzerAnlegenAsync(string? rolle)
    {
        using var scope = Anwendung.Services.CreateScope();
        var benutzerVerwaltung = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        var email = $"benutzer-{Guid.NewGuid():N}@ffw-test.example";
        var benutzer = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };

        Pruefen(await benutzerVerwaltung.CreateAsync(benutzer, Passwort), "Benutzer anlegen");
        if (rolle is not null)
        {
            Pruefen(await benutzerVerwaltung.AddToRoleAsync(benutzer, rolle), "Gruppe zuweisen");
        }

        return email;
    }

    /// <summary>Legt ein Dokument an: Datei im Dokumenten-Verzeichnis und Eintrag in der Datenbank.</summary>
    public async Task<Guid> DokumentAnlegenAsync(string dateiName, byte[] inhalt)
    {
        using var scope = Anwendung.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var speicher = scope.ServiceProvider.GetRequiredService<DokumentSpeicher>();

        var dateiId = Guid.NewGuid();
        await File.WriteAllBytesAsync(speicher.Pfad(dateiId), inhalt);

        db.Dokumente.Add(new Dokument
        {
            Kategorie = new DokumentKategorie { Name = "Kategorie-" + Guid.NewGuid().ToString("N") },
            DateiId = dateiId,
            Name = dateiName,
            Groesse = inhalt.Length,
        });
        await db.SaveChangesAsync();

        return dateiId;
    }

    /// <summary>Führt dieselbe Initialisierung aus wie beim Start der Anwendung (z. B. nach einem Neustart oder Deploy).</summary>
    public Task InitialisierenAsync()
        => DatenbankInitialisierer.InitialisierenAsync(
            Anwendung.Services,
            Anwendung.Services.GetRequiredService<IConfiguration>(),
            NullLogger.Instance);

    private static void Pruefen(IdentityResult ergebnis, string aktion)
    {
        if (!ergebnis.Succeeded)
        {
            throw new InvalidOperationException(
                $"{aktion} fehlgeschlagen: {string.Join("; ", ergebnis.Errors.Select(e => e.Description))}");
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AnwendungCollection : ICollectionFixture<AnwendungFixture>
{
    public const string Name = "Anwendung";
}
