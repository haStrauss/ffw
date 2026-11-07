using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace ffw.Tests.Unit;

/// <summary>Ein eigenes, leeres Verzeichnis pro Test, das am Ende wieder gelöscht wird.</summary>
internal sealed class TempVerzeichnis : IDisposable
{
    public string Pfad { get; } = Path.Combine(Path.GetTempPath(), "ffw-unit-" + Guid.NewGuid().ToString("N"));

    public TempVerzeichnis() => Directory.CreateDirectory(Pfad);

    public string[] Dateien() => Directory.GetFiles(Pfad);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Pfad, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumen ist best effort.
        }
    }
}

/// <summary>Minimale Umgebung, damit die Speicher-Klassen ohne laufende Anwendung erzeugt werden können.</summary>
internal sealed class TestUmgebung : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "ffw.Tests";

    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

    public string WebRootPath { get; set; } = "";

    public string EnvironmentName { get; set; } = "Development";

    public string ContentRootPath { get; set; } = Path.GetTempPath();

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal static class Konfiguration
{
    public static IConfiguration Aus(string schluessel, string wert)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [schluessel] = wert })
            .Build();
}
