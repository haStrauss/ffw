using ffw.Services;
using Microsoft.AspNetCore.DataProtection;

namespace ffw.Tests.Unit;

/// <summary>Die Regeln der Bestenliste, die ohne Datenbank prüfbar sind: Namen, Plausibilität und Spielmarke.</summary>
public class LoeschspielBestenlisteTests
{
    private static LoeschspielBestenliste Neu(IDataProtectionProvider? anbieter = null)
        => new(null!, anbieter ?? new EphemeralDataProtectionProvider());

    // --- Name ---

    [Theory]
    [InlineData("Anna", "Anna")]
    [InlineData("  Max   Muster  ", "Max Muster")]
    [InlineData("Jürgen-Ö.", "Jürgen-Ö.")]
    [InlineData("Strauß 2", "Strauß 2")]
    [InlineData("O'Brien & Co", "O'Brien & Co")]
    public void Gueltige_Namen_werden_bereinigt_angenommen(string eingabe, string erwartet)
        => Assert.Equal(erwartet, LoeschspielBestenliste.NameBereinigen(eingabe));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("Dieser Name ist viel zu lang")]
    [InlineData("<b>fett</b>")]
    [InlineData("Hi‮there")]
    [InlineData("Feuer🔥")]
    [InlineData("a\0b")]
    public void Ungueltige_Namen_werden_abgelehnt(string? eingabe)
        => Assert.Null(LoeschspielBestenliste.NameBereinigen(eingabe));

    [Fact]
    public void Die_Laenge_des_Namens_ist_auf_20_Zeichen_begrenzt()
    {
        Assert.NotNull(LoeschspielBestenliste.NameBereinigen(new string('a', LoeschspielBestenliste.MaxNameLaenge)));
        Assert.Null(LoeschspielBestenliste.NameBereinigen(new string('a', LoeschspielBestenliste.MaxNameLaenge + 1)));
        Assert.Null(LoeschspielBestenliste.NameBereinigen(new string('a', 5000)));
    }

    [Fact]
    public void Zeilenumbrueche_und_Tabs_werden_zu_einem_Leerzeichen()
        => Assert.Equal("Max Muster", LoeschspielBestenliste.NameBereinigen("Max\t\nMuster"));

    // --- Plausibilität ---

    [Theory]
    [InlineData(500, 300, 400, true)]      // normaler Einsatz
    [InlineData(2800, 710, 760, true)]     // sehr guter Einsatz (in der Simulation ca. 4 Punkte pro Sekunde)
    [InlineData(500, 300, 100, false)]     // länger gespielt, als seit dem Start vergangen ist
    [InlineData(10000, 100, 200, false)]   // viel zu viele Punkte für die Dauer
    [InlineData(50, 5, 100, false)]        // zu kurz
    [InlineData(0, 100, 200, false)]       // keine Punkte
    [InlineData(-5, 100, 200, false)]
    public void Plausibilitaet(int punkte, int sekunden, double verstrichen, bool erwartet)
        => Assert.Equal(erwartet, LoeschspielBestenliste.IstPlausibel(punkte, sekunden, verstrichen));

    // --- Spielmarke ---

    [Fact]
    public void Marke_liefert_Kennung_und_verstrichene_Zeit()
    {
        var liste = Neu();
        var start = DateTimeOffset.UtcNow;
        var marke = liste.NeueSpielmarke(start);

        Assert.True(liste.MarkePruefen(marke, start.AddSeconds(30), out var id, out var verstrichen));
        Assert.NotEqual(Guid.Empty, id);
        Assert.InRange(verstrichen, 29, 31);
    }

    [Fact]
    public void Jede_Marke_hat_eine_eigene_Kennung()
    {
        var liste = Neu();
        var jetzt = DateTimeOffset.UtcNow;

        Assert.True(liste.MarkePruefen(liste.NeueSpielmarke(jetzt), jetzt, out var a, out _));
        Assert.True(liste.MarkePruefen(liste.NeueSpielmarke(jetzt), jetzt, out var b, out _));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Veraenderte_Marke_wird_abgelehnt()
    {
        var liste = Neu();
        var jetzt = DateTimeOffset.UtcNow;
        var marke = liste.NeueSpielmarke(jetzt);
        var stelle = marke.Length / 2;
        var veraendert = marke[..stelle] + (marke[stelle] == 'A' ? 'B' : 'A') + marke[(stelle + 1)..];

        Assert.False(liste.MarkePruefen(veraendert, jetzt, out _, out _));
    }

    [Fact]
    public void Marke_einer_anderen_Installation_wird_abgelehnt()
    {
        var jetzt = DateTimeOffset.UtcNow;
        var fremde = Neu(new EphemeralDataProtectionProvider()).NeueSpielmarke(jetzt);

        Assert.False(Neu(new EphemeralDataProtectionProvider()).MarkePruefen(fremde, jetzt, out _, out _));
    }

    [Theory]
    [InlineData(-10)]       // noch vor dem Start
    [InlineData(4 * 3600)]  // länger als 3 Stunden her
    public void Marke_ausserhalb_der_Gueltigkeit_wird_abgelehnt(int sekundenNachStart)
    {
        var liste = Neu();
        var start = DateTimeOffset.UtcNow;
        var marke = liste.NeueSpielmarke(start);

        Assert.False(liste.MarkePruefen(marke, start.AddSeconds(sekundenNachStart), out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein-gueltiges-token")]
    [InlineData("!!!")]
    public void Unsinn_als_Marke_wird_abgelehnt(string? marke)
        => Assert.False(Neu().MarkePruefen(marke, DateTimeOffset.UtcNow, out _, out _));
}
