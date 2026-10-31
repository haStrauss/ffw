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
                // Das Bild wird nacheinander verkleinert: erst Großansicht, dann (aus dieser) die Vorschau.
                VerkleinernAufMaxKante(image, MaxKanteGross);
                await image.SaveAsJpegAsync(PfadGross(id), new JpegEncoder { Quality = 85 }, ct);

                VerkleinernAufMaxKante(image, MaxKanteVorschau);
                await image.SaveAsJpegAsync(PfadVorschau(id), new JpegEncoder { Quality = 78 }, ct);
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

    /// <summary>Verkleinert das Bild direkt (ohne Kopie), falls eine Kante länger als maxKante ist. Vergrößert nie.</summary>
    private static void VerkleinernAufMaxKante(Image bild, int maxKante)
    {
        if (bild.Width <= maxKante && bild.Height <= maxKante)
        {
            return;
        }

        var faktor = (double)maxKante / Math.Max(bild.Width, bild.Height);
        var breite = Math.Max(1, (int)Math.Round(bild.Width * faktor));
        var hoehe = Math.Max(1, (int)Math.Round(bild.Height * faktor));
        bild.Mutate(x => x.Resize(breite, hoehe));
    }
}
