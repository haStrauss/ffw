namespace ffw.Services;

/// <summary>
/// Speichert hochgeladene Dokumente im Dateisystem. Erlaubt sind nur eine feste Liste von Dateitypen
/// (PDF, Office, OpenDocument, Text). Zusätzlich wird geprüft, ob der Dateianfang zur Endung passt,
/// damit nicht einfach etwas anderes mit einer harmlosen Endung abgelegt wird.
/// Auf der Platte heißt jede Datei nur wie eine zufällige GUID, der Originalname steht in der Datenbank.
/// </summary>
public sealed class DokumentSpeicher
{
    private const int KopfBytes = 8192;

    private static readonly byte[] PdfKennung = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipKennung = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] OleKennung = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public sealed record Dateityp(string ContentType, bool ImBrowserAnzeigen);

    private enum Aufbau { Pdf, Zip, Ole, Text }

    private static readonly Dictionary<string, (Dateityp Typ, Aufbau Aufbau)> Erlaubt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = (new Dateityp("application/pdf", true), Aufbau.Pdf),
        [".doc"] = (new Dateityp("application/msword", false), Aufbau.Ole),
        [".docx"] = (new Dateityp("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false), Aufbau.Zip),
        [".xls"] = (new Dateityp("application/vnd.ms-excel", false), Aufbau.Ole),
        [".xlsx"] = (new Dateityp("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false), Aufbau.Zip),
        [".ppt"] = (new Dateityp("application/vnd.ms-powerpoint", false), Aufbau.Ole),
        [".pptx"] = (new Dateityp("application/vnd.openxmlformats-officedocument.presentationml.presentation", false), Aufbau.Zip),
        [".odt"] = (new Dateityp("application/vnd.oasis.opendocument.text", false), Aufbau.Zip),
        [".ods"] = (new Dateityp("application/vnd.oasis.opendocument.spreadsheet", false), Aufbau.Zip),
        [".odp"] = (new Dateityp("application/vnd.oasis.opendocument.presentation", false), Aufbau.Zip),
        [".txt"] = (new Dateityp("text/plain; charset=utf-8", false), Aufbau.Text),
        [".csv"] = (new Dateityp("text/csv; charset=utf-8", false), Aufbau.Text),
    };

    /// <summary>Für den Hinweistext im Upload-Formular und für das accept-Attribut.</summary>
    public static IEnumerable<string> ErlaubteEndungen => Erlaubt.Keys;

    public string Verzeichnis { get; }

    public DokumentSpeicher(IConfiguration config, IWebHostEnvironment env)
    {
        var pfad = config["Dokumente:Speicherpfad"];
        Verzeichnis = Path.GetFullPath(string.IsNullOrWhiteSpace(pfad)
            ? Path.Combine(env.ContentRootPath, "data", "dokumente")
            : pfad);
        Directory.CreateDirectory(Verzeichnis);
    }

    public string Pfad(Guid id) => Path.Combine(Verzeichnis, id.ToString("N") + ".dat");

    /// <summary>Dateityp zur Endung des Dateinamens, oder null, wenn die Endung nicht erlaubt ist.</summary>
    public static Dateityp? TypFuer(string dateiName)
        => Erlaubt.TryGetValue(Path.GetExtension(dateiName), out var eintrag) ? eintrag.Typ : null;

    /// <summary>Gibt die Id der gespeicherten Datei zurück oder null, wenn Endung oder Inhalt nicht erlaubt sind.</summary>
    public async Task<Guid?> SpeichernAsync(Stream upload, string dateiName, CancellationToken ct = default)
    {
        if (!Erlaubt.TryGetValue(Path.GetExtension(dateiName), out var eintrag))
        {
            return null;
        }

        var kopf = new byte[KopfBytes];
        var gelesen = await upload.ReadAtLeastAsync(kopf, kopf.Length, throwOnEndOfStream: false, ct);
        if (gelesen == 0 || !InhaltPasst(eintrag.Aufbau, kopf.AsSpan(0, gelesen)))
        {
            return null;
        }

        var id = Guid.NewGuid();
        try
        {
            await using var ziel = new FileStream(Pfad(id), FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);
            await ziel.WriteAsync(kopf.AsMemory(0, gelesen), ct);
            await upload.CopyToAsync(ziel, ct);
        }
        catch
        {
            Loeschen(id);
            throw;
        }

        return id;
    }

    public void Loeschen(Guid id)
    {
        try
        {
            File.Delete(Pfad(id));
        }
        catch (IOException)
        {
            // Aufräumen ist best effort.
        }
    }

    private static bool InhaltPasst(Aufbau aufbau, ReadOnlySpan<byte> kopf) => aufbau switch
    {
        // Manche PDFs haben vor der Kennung noch ein paar Bytes Vorspann.
        Aufbau.Pdf => kopf[..Math.Min(kopf.Length, 1024)].IndexOf(PdfKennung.AsSpan()) >= 0,
        Aufbau.Zip => kopf.StartsWith(ZipKennung.AsSpan()),
        Aufbau.Ole => kopf.StartsWith(OleKennung.AsSpan()),
        // Reiner Text enthält keine Nullbytes.
        Aufbau.Text => !kopf.Contains((byte)0),
        _ => false,
    };
}
