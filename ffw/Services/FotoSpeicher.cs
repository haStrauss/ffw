using SkiaSharp;

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

    /// <summary>
    /// Nur diese Formate werden angenommen (dieselben, die die Foto-Seite zur Auswahl anbietet). Alles andere, auch
    /// TIFF oder BMP, wird abgelehnt, bevor ein einziger Pixel dekodiert wird. Das hält die Angriffsfläche klein.
    /// </summary>
    private static readonly SKEncodedImageFormat[] ErlaubteFormate =
    [
        SKEncodedImageFormat.Jpeg,
        SKEncodedImageFormat.Png,
        SKEncodedImageFormat.Webp,
        SKEncodedImageFormat.Gif,
    ];

    private static readonly SKSamplingOptions Verkleinerung = new(SKFilterMode.Linear, SKMipmapMode.Linear);

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
        puffer.Position = 0;

        // Zuerst wird nur der Dateikopf gelesen: Format und Größe stehen fest, bevor irgendein Pixel dekodiert wird.
        using var codec = SKCodec.Create(puffer, out var ergebnis);
        if (codec is null || ergebnis != SKCodecResult.Success || !ErlaubteFormate.Contains(codec.EncodedFormat))
        {
            return null;
        }

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixel)
        {
            return null;
        }

        using var quelle = SKBitmap.Decode(codec);
        if (quelle is null)
        {
            return null;
        }

        // Die Großansicht wird aus dem Original erzeugt (verkleinert und nach den Kameradaten gedreht),
        // die Vorschau danach aus der Großansicht.
        using var gross = Aufbereiten(quelle, codec.EncodedOrigin, MaxKanteGross);
        using var vorschau = Aufbereiten(gross, SKEncodedOrigin.TopLeft, MaxKanteVorschau);

        var id = Guid.NewGuid();
        try
        {
            SpeichernAlsJpeg(gross, PfadGross(id), qualitaet: 85);
            SpeichernAlsJpeg(vorschau, PfadVorschau(id), qualitaet: 78);
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

    /// <summary>
    /// Verkleinert das Bild, falls eine Kante länger als maxKante ist (vergrößert nie), und dreht es so, dass es
    /// aufrecht steht. Gibt immer ein neues, undurchsichtiges Bild zurück, das der Aufrufer freigibt.
    /// </summary>
    internal static SKBitmap Aufbereiten(SKBitmap quelle, SKEncodedOrigin ausrichtung, int maxKante)
    {
        SKBitmap? verkleinert = null;
        try
        {
            var basis = quelle;
            if (quelle.Width > maxKante || quelle.Height > maxKante)
            {
                var faktor = (double)maxKante / Math.Max(quelle.Width, quelle.Height);
                var breite = Math.Max(1, (int)Math.Round(quelle.Width * faktor));
                var hoehe = Math.Max(1, (int)Math.Round(quelle.Height * faktor));
                verkleinert = quelle.Resize(new SKSizeI(breite, hoehe), Verkleinerung)
                    ?? throw new InvalidOperationException("Das Bild konnte nicht verkleinert werden.");
                basis = verkleinert;
            }

            return Ausrichten(basis, ausrichtung);
        }
        finally
        {
            verkleinert?.Dispose();
        }
    }

    /// <summary>
    /// Dreht bzw. spiegelt das Bild nach der EXIF-Ausrichtung (Werte 1 bis 8) und legt es auf weißen Grund,
    /// denn JPEG kennt keine Transparenz. Gibt immer ein neues Bild zurück.
    /// </summary>
    internal static SKBitmap Ausrichten(SKBitmap bild, SKEncodedOrigin ausrichtung)
    {
        int w = bild.Width, h = bild.Height;
        var hochkant = ausrichtung is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        var ziel = new SKBitmap(hochkant ? h : w, hochkant ? w : h, isOpaque: true);
        try
        {
            using var leinwand = new SKCanvas(ziel);
            leinwand.Clear(SKColors.White);
            leinwand.SetMatrix(MatrixFuer(ausrichtung, w, h));
            leinwand.DrawBitmap(bild, 0, 0);
            return ziel;
        }
        catch
        {
            ziel.Dispose();
            throw;
        }
    }

    /// <summary>Wohin ein Pixel (x, y) des Originals wandert, damit das Bild aufrecht steht (w, h: Größe des Originals).</summary>
    private static SKMatrix MatrixFuer(SKEncodedOrigin ausrichtung, int w, int h) => ausrichtung switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.CreateIdentity(),
    };

    private static void SpeichernAlsJpeg(SKBitmap bild, string pfad, int qualitaet)
    {
        using var datei = File.Create(pfad);
        if (!bild.Encode(datei, SKEncodedImageFormat.Jpeg, qualitaet))
        {
            throw new IOException("Das Bild konnte nicht als JPEG gespeichert werden.");
        }
    }
}
