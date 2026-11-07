using System.Text;
using ffw.Services;

namespace ffw.Tests.Unit;

public class DokumentSpeicherTests : IDisposable
{
    private readonly TempVerzeichnis _verzeichnis = new();
    private readonly DokumentSpeicher _speicher;

    public DokumentSpeicherTests()
    {
        _speicher = new DokumentSpeicher(
            Konfiguration.Aus("Dokumente:Speicherpfad", _verzeichnis.Pfad),
            new TestUmgebung());
    }

    public void Dispose() => _verzeichnis.Dispose();

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< >>\nendobj\n");
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00];

    private Task<Guid?> SpeichernAsync(byte[] inhalt, string dateiName)
        => _speicher.SpeichernAsync(new MemoryStream(inhalt), dateiName);

    [Theory]
    [InlineData("Satzung.pdf", "application/pdf", true)]
    [InlineData("SATZUNG.PDF", "application/pdf", true)]
    [InlineData("Antrag.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)]
    [InlineData("Liste.csv", "text/csv; charset=utf-8", false)]
    [InlineData("Notiz.txt", "text/plain; charset=utf-8", false)]
    public void Erlaubte_Endungen_haben_den_richtigen_Dateityp(string name, string contentType, bool imBrowser)
    {
        var typ = DokumentSpeicher.TypFuer(name);

        Assert.NotNull(typ);
        Assert.Equal(contentType, typ.ContentType);
        Assert.Equal(imBrowser, typ.ImBrowserAnzeigen);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("seite.html")]
    [InlineData("skript.js")]
    [InlineData("bild.svg")]
    [InlineData("archiv.pdf.exe")]
    [InlineData("ohneendung")]
    [InlineData("")]
    public void Nicht_erlaubte_Endungen_haben_keinen_Dateityp(string name)
    {
        Assert.Null(DokumentSpeicher.TypFuer(name));
    }

    [Fact]
    public void Nur_PDF_wird_im_Browser_angezeigt()
    {
        var imBrowser = DokumentSpeicher.ErlaubteEndungen
            .Where(endung => DokumentSpeicher.TypFuer("datei" + endung)!.ImBrowserAnzeigen)
            .ToList();

        Assert.Equal(new[] { ".pdf" }, imBrowser);
    }

    [Fact]
    public async Task Gueltiges_PDF_wird_unter_einer_zufaelligen_Id_gespeichert()
    {
        var id = await SpeichernAsync(Pdf, "Satzung.pdf");

        Assert.NotNull(id);
        var pfad = _speicher.Pfad(id.Value);
        Assert.Equal(Pdf, await File.ReadAllBytesAsync(pfad));
        Assert.EndsWith(".dat", pfad);
        Assert.Equal(_verzeichnis.Pfad, Path.GetDirectoryName(pfad));
    }

    [Fact]
    public async Task PDF_mit_kurzem_Vorspann_wird_akzeptiert()
    {
        var inhalt = Encoding.ASCII.GetBytes(new string('x', 100) + "%PDF-1.4\n").ToArray();

        Assert.NotNull(await SpeichernAsync(inhalt, "scan.pdf"));
    }

    [Fact]
    public async Task PDF_mit_zu_langem_Vorspann_wird_abgelehnt()
    {
        var inhalt = Encoding.ASCII.GetBytes(new string('x', 1100) + "%PDF-1.4\n");

        Assert.Null(await SpeichernAsync(inhalt, "scan.pdf"));
        Assert.Empty(_verzeichnis.Dateien());
    }

    [Theory]
    [InlineData("Antrag.docx")]
    [InlineData("Tabelle.xlsx")]
    [InlineData("Folien.pptx")]
    [InlineData("Brief.odt")]
    public async Task Office_Dateien_im_Zip_Format_werden_akzeptiert(string name)
    {
        Assert.NotNull(await SpeichernAsync(Zip, name));
    }

    [Theory]
    [InlineData("Alt.doc")]
    [InlineData("Alt.xls")]
    [InlineData("Alt.ppt")]
    public async Task Alte_Office_Dateien_werden_akzeptiert(string name)
    {
        Assert.NotNull(await SpeichernAsync(Ole, name));
    }

    [Fact]
    public async Task Nicht_erlaubte_Endung_wird_abgelehnt_und_nichts_gespeichert()
    {
        Assert.Null(await SpeichernAsync(Pdf, "virus.exe"));
        Assert.Empty(_verzeichnis.Dateien());
    }

    [Theory]
    [InlineData("MZ-Programm", "falsch.pdf")]
    [InlineData("%PDF-1.7", "falsch.docx")]
    [InlineData("%PDF-1.7", "falsch.doc")]
    public async Task Inhalt_der_nicht_zur_Endung_passt_wird_abgelehnt(string inhalt, string name)
    {
        Assert.Null(await SpeichernAsync(Encoding.ASCII.GetBytes(inhalt), name));
        Assert.Empty(_verzeichnis.Dateien());
    }

    [Fact]
    public async Task Zip_Inhalt_mit_Endung_doc_wird_abgelehnt()
    {
        Assert.Null(await SpeichernAsync(Zip, "falsch.doc"));
    }

    [Fact]
    public async Task Text_ohne_Nullbytes_wird_akzeptiert()
    {
        Assert.NotNull(await SpeichernAsync(Encoding.UTF8.GetBytes("Hallo Wörter, Größe\n"), "Notiz.txt"));
    }

    [Fact]
    public async Task Text_mit_Nullbyte_wird_abgelehnt()
    {
        Assert.Null(await SpeichernAsync([0x48, 0x00, 0x49], "Notiz.txt"));
    }

    [Fact]
    public async Task Leere_Datei_wird_abgelehnt()
    {
        Assert.Null(await SpeichernAsync([], "leer.txt"));
        Assert.Empty(_verzeichnis.Dateien());
    }

    [Fact]
    public async Task Grosse_Datei_wird_vollstaendig_gespeichert()
    {
        var inhalt = new byte[50_000];
        Pdf.CopyTo(inhalt, 0);
        for (var i = Pdf.Length; i < inhalt.Length; i++)
        {
            inhalt[i] = (byte)(i % 251);
        }

        var id = await SpeichernAsync(inhalt, "gross.pdf");

        Assert.NotNull(id);
        Assert.Equal(inhalt, await File.ReadAllBytesAsync(_speicher.Pfad(id.Value)));
    }

    [Fact]
    public async Task Loeschen_entfernt_die_Datei()
    {
        var id = (await SpeichernAsync(Pdf, "Satzung.pdf"))!.Value;

        _speicher.Loeschen(id);

        Assert.False(File.Exists(_speicher.Pfad(id)));
    }

    [Fact]
    public void Loeschen_einer_nicht_vorhandenen_Datei_ist_kein_Fehler()
    {
        var ausnahme = Record.Exception(() => _speicher.Loeschen(Guid.NewGuid()));

        Assert.Null(ausnahme);
    }
}
