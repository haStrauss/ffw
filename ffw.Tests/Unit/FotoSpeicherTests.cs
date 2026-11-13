using System.Text;
using ffw.Services;
using SkiaSharp;

namespace ffw.Tests.Unit;

public class FotoSpeicherTests : IDisposable
{
    private readonly TempVerzeichnis _verzeichnis = new();
    private readonly FotoSpeicher _speicher;

    public FotoSpeicherTests()
    {
        _speicher = new FotoSpeicher(
            Konfiguration.Aus("Fotos:Speicherpfad", _verzeichnis.Pfad),
            new TestUmgebung());
    }

    public void Dispose() => _verzeichnis.Dispose();

    // --- Testbilder ---

    private static byte[] Bild(int breite, int hoehe, SKEncodedImageFormat format, SKColor? farbe = null)
    {
        using var bitmap = new SKBitmap(breite, hoehe);
        bitmap.Erase(farbe ?? SKColors.SteelBlue);
        using var daten = bitmap.Encode(format, 90);
        return daten.ToArray();
    }

    private static MemoryStream Png(int breite, int hoehe) => new(Bild(breite, hoehe, SKEncodedImageFormat.Png));

    private static SKImageInfo Info(string pfad)
    {
        using var codec = SKCodec.Create(pfad);
        Assert.NotNull(codec);
        return codec.Info;
    }

    // --- Verkleinern ---

    [Fact]
    public async Task Grosses_Querformat_wird_auf_2000_und_480_Pixel_verkleinert()
    {
        var id = await _speicher.SpeichernAsync(Png(4000, 3000));

        Assert.NotNull(id);
        var gross = Info(_speicher.PfadGross(id.Value));
        var vorschau = Info(_speicher.PfadVorschau(id.Value));
        Assert.Equal((2000, 1500), (gross.Width, gross.Height));
        Assert.Equal((480, 360), (vorschau.Width, vorschau.Height));
    }

    [Fact]
    public async Task Hochformat_wird_an_der_laengeren_Kante_verkleinert()
    {
        var id = await _speicher.SpeichernAsync(Png(1500, 3000));

        Assert.NotNull(id);
        var gross = Info(_speicher.PfadGross(id.Value));
        var vorschau = Info(_speicher.PfadVorschau(id.Value));
        Assert.Equal((1000, 2000), (gross.Width, gross.Height));
        Assert.Equal((240, 480), (vorschau.Width, vorschau.Height));
    }

    [Fact]
    public async Task Kleine_Bilder_werden_nicht_vergroessert()
    {
        var id = await _speicher.SpeichernAsync(Png(300, 200));

        Assert.NotNull(id);
        var gross = Info(_speicher.PfadGross(id.Value));
        var vorschau = Info(_speicher.PfadVorschau(id.Value));
        Assert.Equal((300, 200), (gross.Width, gross.Height));
        Assert.Equal((300, 200), (vorschau.Width, vorschau.Height));
    }

    [Fact]
    public async Task Gespeichert_wird_immer_ein_JPEG_auch_wenn_ein_PNG_hochgeladen_wurde()
    {
        var id = await _speicher.SpeichernAsync(Png(100, 100));

        Assert.NotNull(id);
        foreach (var pfad in new[] { _speicher.PfadGross(id.Value), _speicher.PfadVorschau(id.Value) })
        {
            var anfang = (await File.ReadAllBytesAsync(pfad)).Take(3).ToArray();
            Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, anfang);
        }
    }

    // --- Erlaubte Formate ---

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Jpeg_Png_und_WebP_werden_angenommen(SKEncodedImageFormat format)
    {
        var id = await _speicher.SpeichernAsync(new MemoryStream(Bild(64, 48, format)));

        Assert.NotNull(id);
        Assert.Equal((64, 48), (Info(_speicher.PfadGross(id.Value)).Width, Info(_speicher.PfadGross(id.Value)).Height));
    }

    [Fact]
    public async Task Gif_wird_angenommen()
    {
        // Das kleinste gültige GIF: ein weißer Pixel.
        byte[] gif =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x00, 0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
        ];

        Assert.NotNull(await _speicher.SpeichernAsync(new MemoryStream(gif)));
    }

    [Fact]
    public async Task BMP_wird_abgelehnt_obwohl_es_ein_gueltiges_Bild_ist()
    {
        // Ein gültiges 1x1-BMP: Es wäre dekodierbar, gehört aber nicht zu den erlaubten Formaten.
        byte[] bmp =
        [
            0x42, 0x4D, 0x3A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
            0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x18, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00,
        ];

        Assert.Null(await _speicher.SpeichernAsync(new MemoryStream(bmp)));
        Assert.Empty(_verzeichnis.Dateien());
    }

    [Fact]
    public async Task TIFF_wird_abgelehnt()
    {
        // Ein TIFF-Kopf (little endian) mit einem leeren Verzeichnis. Der TIFF-Dekoder war Ziel mehrerer Sicherheitslücken
        // und wird deshalb gar nicht erst angesprochen.
        byte[] tiff = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        Assert.Null(await _speicher.SpeichernAsync(new MemoryStream(tiff)));
        Assert.Empty(_verzeichnis.Dateien());
    }

    [Fact]
    public async Task Datei_die_kein_Bild_ist_wird_abgelehnt()
    {
        var upload = new MemoryStream(Encoding.UTF8.GetBytes("Das ist kein Bild."));

        Assert.Null(await _speicher.SpeichernAsync(upload));
        Assert.Empty(_verzeichnis.Dateien());
    }

    [Fact]
    public async Task Leerer_Upload_wird_abgelehnt()
    {
        Assert.Null(await _speicher.SpeichernAsync(new MemoryStream()));
        Assert.Empty(_verzeichnis.Dateien());
    }

    [Fact]
    public async Task Bild_mit_unzumutbar_vielen_Pixeln_wird_abgelehnt()
    {
        // Nur der Kopf einer 20000 x 20000 Pixel großen PNG-Datei (400 Millionen Pixel, Grenze: 100 Millionen).
        // Das Bild darf gar nicht erst dekodiert werden, sonst wäre der Speicher des Servers schnell voll.
        var upload = new MemoryStream(PngKopf(20000, 20000));

        Assert.Null(await _speicher.SpeichernAsync(upload));
        Assert.Empty(_verzeichnis.Dateien());
    }

    // --- Drehen und Transparenz ---

    [Fact]
    public async Task Kamera_Drehung_aus_den_EXIF_Daten_wird_angewendet()
    {
        // Links rot, rechts blau, 40 x 20 Pixel, mit der Kennung "um 90 Grad im Uhrzeigersinn drehen" (EXIF-Wert 6).
        // Danach muss das Bild 20 x 40 groß sein, mit Rot oben und Blau unten.
        using var bitmap = new SKBitmap(40, 20);
        bitmap.Erase(SKColors.Red);
        for (var x = 20; x < 40; x++)
        {
            for (var y = 0; y < 20; y++)
            {
                bitmap.SetPixel(x, y, SKColors.Blue);
            }
        }

        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 95);
        var id = await _speicher.SpeichernAsync(new MemoryStream(MitExifDrehung(jpeg.ToArray(), drehung: 6)));

        Assert.NotNull(id);
        using var ergebnis = SKBitmap.Decode(_speicher.PfadGross(id.Value));
        Assert.Equal((20, 40), (ergebnis.Width, ergebnis.Height));
        var oben = ergebnis.GetPixel(10, 5);
        var unten = ergebnis.GetPixel(10, 35);
        Assert.True(oben.Red > 200 && oben.Blue < 60, $"oben sollte rot sein, ist aber {oben}");
        Assert.True(unten.Blue > 200 && unten.Red < 60, $"unten sollte blau sein, ist aber {unten}");
    }

    [Fact]
    public async Task Transparenz_wird_zu_weissem_Hintergrund()
    {
        // JPEG kennt keine Transparenz. Ohne Gegenmaßnahme würden durchsichtige Stellen schwarz.
        var id = await _speicher.SpeichernAsync(new MemoryStream(Bild(32, 32, SKEncodedImageFormat.Png, SKColors.Transparent)));

        Assert.NotNull(id);
        using var ergebnis = SKBitmap.Decode(_speicher.PfadGross(id.Value));
        var pixel = ergebnis.GetPixel(16, 16);
        Assert.True(pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245, $"erwartet weiß, ist aber {pixel}");
    }

    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft)]
    [InlineData(SKEncodedOrigin.TopRight)]
    [InlineData(SKEncodedOrigin.BottomRight)]
    [InlineData(SKEncodedOrigin.BottomLeft)]
    [InlineData(SKEncodedOrigin.LeftTop)]
    [InlineData(SKEncodedOrigin.RightTop)]
    [InlineData(SKEncodedOrigin.RightBottom)]
    [InlineData(SKEncodedOrigin.LeftBottom)]
    public void Ausrichten_verschiebt_jeden_Pixel_wie_in_der_EXIF_Norm_beschrieben(SKEncodedOrigin ausrichtung)
    {
        const int w = 3, h = 2;
        using var quelle = new SKBitmap(w, h, isOpaque: true);
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                quelle.SetPixel(x, y, new SKColor((byte)(40 * x + 10), (byte)(100 * y + 20), 77));
            }
        }

        using var ziel = FotoSpeicher.Ausrichten(quelle, ausrichtung);

        var hochkant = ausrichtung is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        Assert.Equal(hochkant ? h : w, ziel.Width);
        Assert.Equal(hochkant ? w : h, ziel.Height);

        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var (neuX, neuY) = ZielPixel(ausrichtung, x, y, w, h);
                Assert.Equal(quelle.GetPixel(x, y), ziel.GetPixel(neuX, neuY));
            }
        }
    }

    private static (int X, int Y) ZielPixel(SKEncodedOrigin ausrichtung, int x, int y, int w, int h) => ausrichtung switch
    {
        SKEncodedOrigin.TopLeft => (x, y),
        SKEncodedOrigin.TopRight => (w - 1 - x, y),             // horizontal spiegeln
        SKEncodedOrigin.BottomRight => (w - 1 - x, h - 1 - y),  // um 180 Grad drehen
        SKEncodedOrigin.BottomLeft => (x, h - 1 - y),           // vertikal spiegeln
        SKEncodedOrigin.LeftTop => (y, x),                      // an der Diagonale spiegeln
        SKEncodedOrigin.RightTop => (h - 1 - y, x),             // um 90 Grad im Uhrzeigersinn drehen
        SKEncodedOrigin.RightBottom => (h - 1 - y, w - 1 - x),  // an der anderen Diagonale spiegeln
        SKEncodedOrigin.LeftBottom => (y, w - 1 - x),           // um 90 Grad gegen den Uhrzeigersinn drehen
        _ => throw new ArgumentOutOfRangeException(nameof(ausrichtung)),
    };

    // --- Aufräumen und Ids ---

    [Fact]
    public async Task Loeschen_entfernt_Grossansicht_und_Vorschau()
    {
        var id = (await _speicher.SpeichernAsync(Png(100, 100)))!.Value;

        _speicher.Loeschen(id);

        Assert.False(File.Exists(_speicher.PfadGross(id)));
        Assert.False(File.Exists(_speicher.PfadVorschau(id)));
    }

    [Fact]
    public async Task Jedes_Bild_bekommt_eine_eigene_Id()
    {
        var erste = await _speicher.SpeichernAsync(Png(50, 50));
        var zweite = await _speicher.SpeichernAsync(Png(50, 50));

        Assert.NotEqual(erste, zweite);
        Assert.Equal(4, _verzeichnis.Dateien().Length);
    }

    // --- Hilfen, um Testdateien mit bestimmten Eigenschaften zu bauen ---

    /// <summary>Fügt direkt hinter dem Dateianfang (FF D8) einen EXIF-Block mit der Drehung (Tag 0x0112) ein.</summary>
    private static byte[] MitExifDrehung(byte[] jpeg, byte drehung)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,                         // APP1, Länge 34
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,             // "Exif"
            0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, // TIFF-Kopf (big endian), erstes Verzeichnis bei Byte 8
            0x00, 0x01,                                     // ein Eintrag
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, drehung, 0x00, 0x00, // Drehung, Typ SHORT
            0x00, 0x00, 0x00, 0x00,                         // kein weiteres Verzeichnis
        ];

        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpeg.Take(2).ToArray());
        return [.. jpeg.Take(2), .. exif, .. jpeg.Skip(2)];
    }

    private static byte[] PngKopf(uint breite, uint hoehe)
    {
        using var strom = new MemoryStream();
        strom.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new List<byte>();
        ihdr.AddRange(GrossesEnde(breite));
        ihdr.AddRange(GrossesEnde(hoehe));
        ihdr.AddRange(new byte[] { 8, 2, 0, 0, 0 }); // 8 Bit, RGB, keine Interlace-Verfahren
        Chunk(strom, "IHDR", ihdr.ToArray());

        // Ein minimaler, gültiger (leerer) zlib-Datenstrom als Bilddaten.
        Chunk(strom, "IDAT", new byte[] { 0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 });
        Chunk(strom, "IEND", Array.Empty<byte>());
        return strom.ToArray();
    }

    private static void Chunk(Stream strom, string typ, byte[] daten)
    {
        var typBytes = Encoding.ASCII.GetBytes(typ);
        strom.Write(GrossesEnde((uint)daten.Length));
        strom.Write(typBytes);
        strom.Write(daten);
        strom.Write(GrossesEnde(Crc32(typBytes.Concat(daten).ToArray())));
    }

    private static byte[] GrossesEnde(uint wert)
        => [(byte)(wert >> 24), (byte)(wert >> 16), (byte)(wert >> 8), (byte)wert];

    private static uint Crc32(byte[] daten)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in daten)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
