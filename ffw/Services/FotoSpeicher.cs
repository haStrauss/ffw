using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ffw.Services;

/// <summary>
/// Speichert hochgeladene Fotos im Dateisystem. Jedes Bild wird dabei vollständig dekodiert und neu als JPEG
/// geschrieben (Großansicht und Vorschau). Das verhindert, dass etwas anderes als ein echtes Bild abgelegt wird,
/// spart Platz und entfernt Metadaten wie GPS-Koordinaten.
/// </summary>
public sealed class FotoSpeicher
{
    private const int MaxKanteGross = 2000;
    private const int MaxKanteVorschau = 480;
    private const long MaxPixel = 100_000_000;

    public string Verzeichnis { get; }

    public FotoSpeicher(IConfiguration config, IWebHostEnvironment env)
    {
        var pfad = config["Fotos:Speicherpfad"];
        Verzeichnis = Path.GetFullPath(string.IsNullOrWhiteSpace(pfad)
            ? Path.Combine(env.ContentRootPath, "data", "fotos")
            : pfad);
        Directory.CreateDirectory(Verzeichnis);
    }

    public string PfadGross(Guid id) => Path.Combine(Verzeichnis, id.ToString("N") + ".jpg");

    public string PfadVorschau(Guid id) => Path.Combine(Verzeichnis, id.ToString("N") + "_t.jpg");

    /// <summary>Gibt die Id der gespeicherten Datei zurück oder null, wenn der Upload kein verwendbares Bild ist.</summary>
    public async Task<Guid?> SpeichernAsync(Stream upload, CancellationToken ct = default)
    {
        using var puffer = new MemoryStream();
        await upload.CopyToAsync(puffer, ct);

        Image image;
        try
        {
            puffer.Position = 0;
            var info = await Image.IdentifyAsync(puffer, ct);
            if ((long)info.Width * info.Height > MaxPixel)
            {
                return null;
            }

            puffer.Position = 0;
            image = await Image.LoadAsync(puffer, ct);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return null;
        }

        using (image)
        {
            image.Mutate(x => x.AutoOrient());

            var id = Guid.NewGuid();
            try
            {
                await SchreibenAsync(image, MaxKanteGross, 85, PfadGross(id), ct);
                await SchreibenAsync(image, MaxKanteVorschau, 78, PfadVorschau(id), ct);
            }
            catch
            {
                Loeschen(id);
                throw;
            }

            return id;
        }
    }

    public void Loeschen(Guid id)
    {
        foreach (var pfad in new[] { PfadGross(id), PfadVorschau(id) })
        {
            try
            {
                File.Delete(pfad);
            }
            catch (IOException)
            {
                // Aufräumen ist best effort.
            }
        }
    }

    private static async Task SchreibenAsync(Image quelle, int maxKante, int qualitaet, string pfad, CancellationToken ct)
    {
        using var kopie = Verkleinert(quelle, maxKante);
        await kopie.SaveAsJpegAsync(pfad, new JpegEncoder { Quality = qualitaet }, ct);
    }

    private static Image Verkleinert(Image quelle, int maxKante)
    {
        if (quelle.Width <= maxKante && quelle.Height <= maxKante)
        {
            return quelle.Clone();
        }

        var faktor = (double)maxKante / Math.Max(quelle.Width, quelle.Height);
        var breite = Math.Max(1, (int)Math.Round(quelle.Width * faktor));
        var hoehe = Math.Max(1, (int)Math.Round(quelle.Height * faktor));
        return quelle.Clone(x => x.Resize(breite, hoehe));
    }
}
