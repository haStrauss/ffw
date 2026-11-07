using System.Text;
using ffw.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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

    private static MemoryStream Png(int breite, int hoehe)
    {
        using var bild = new Image<Rgb24>(breite, hoehe);
        var strom = new MemoryStream();
        bild.SaveAsPng(strom);
        strom.Position = 0;
        return strom;
    }

    [Fact]
    public async Task Grosses_Querformat_wird_auf_2000_und_480_Pixel_verkleinert()
    {
        var id = await _speicher.SpeichernAsync(Png(4000, 3000));

        Assert.NotNull(id);
        var gross = Image.Identify(_speicher.PfadGross(id.Value));
        var vorschau = Image.Identify(_speicher.PfadVorschau(id.Value));
        Assert.Equal((2000, 1500), (gross.Width, gross.Height));
        Assert.Equal((480, 360), (vorschau.Width, vorschau.Height));
    }

    [Fact]
    public async Task Hochformat_wird_an_der_laengeren_Kante_verkleinert()
    {
        var id = await _speicher.SpeichernAsync(Png(1500, 3000));

        Assert.NotNull(id);
        var gross = Image.Identify(_speicher.PfadGross(id.Value));
        var vorschau = Image.Identify(_speicher.PfadVorschau(id.Value));
        Assert.Equal((1000, 2000), (gross.Width, gross.Height));
        Assert.Equal((240, 480), (vorschau.Width, vorschau.Height));
    }

    [Fact]
    public async Task Kleine_Bilder_werden_nicht_vergroessert()
    {
        var id = await _speicher.SpeichernAsync(Png(300, 200));

        Assert.NotNull(id);
        var gross = Image.Identify(_speicher.PfadGross(id.Value));
        var vorschau = Image.Identify(_speicher.PfadVorschau(id.Value));
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

    // --- Hilfen, um einen PNG-Kopf mit beliebiger Größe zu bauen ---

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
