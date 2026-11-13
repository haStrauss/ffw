using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ffw.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ffw.Services;

/// <summary>
/// Die Bestenliste (Top 10) des Löschspiels. Das Spiel läuft komplett im Browser, deshalb kann der Server ein Ergebnis nicht
/// nachspielen. Er prüft stattdessen, ob es möglich ist: Beim Start eines Einsatzes bekommt der Browser eine Spielmarke
/// (verschlüsselt und manipulationssicher, mit Kennung und Startzeit). Ein Ergebnis wird nur mit gültiger Marke angenommen,
/// nur einmal pro Marke, die Einsatzdauer darf nicht länger sein als die Zeit seit dem Start, und die Punkte müssen zur Dauer passen.
/// Ein Spieler mit Entwicklerwerkzeugen kann sich trotzdem etwas hineinschummeln, das lässt sich bei einem Browserspiel nicht verhindern.
/// </summary>
public sealed class LoeschspielBestenliste
{
    public const int Plaetze = 10;
    public const int MinNameLaenge = 2;
    public const int MaxNameLaenge = 20;

    /// <summary>So viele Einträge bleiben gespeichert, damit die Tabelle nicht ewig wächst.</summary>
    private const int MaxGespeichert = 100;

    private const int MinSekunden = 10;

    // Ein guter Spieler schafft im Spiel etwa 4 Punkte pro Sekunde, hier ist reichlich Luft darüber.
    private const double Grundpunkte = 40;
    private const double MaxPunkteProSekunde = 12;

    private static readonly TimeSpan MarkeGueltig = TimeSpan.FromHours(3);

    /// <summary>Außer Buchstaben, Ziffern und Leerzeichen sind nur diese Zeichen in Namen erlaubt.</summary>
    public const string ErlaubteSonderzeichen = "-_.'&+!?()";

    public sealed record Eintrag(int Platz, string Name, int Punkte, int Sekunden);

    public enum Ausgang
    {
        Eingetragen,
        MarkeUngueltig,
        NameUngueltig,
        Unplausibel,
        NichtPlatziert,
        BereitsEingetragen,
    }

    public sealed record Eintragung(Ausgang Ausgang, int? Platz);

    private readonly AppDbContext _db;
    private readonly IDataProtector _schutz;

    public LoeschspielBestenliste(AppDbContext db, IDataProtectionProvider schutzAnbieter)
    {
        _db = db;
        _schutz = schutzAnbieter.CreateProtector("ffw.Loeschspiel.Spielmarke");
    }

    // --- Spielmarke ---

    public string NeueSpielmarke() => NeueSpielmarke(DateTimeOffset.UtcNow);

    public string NeueSpielmarke(DateTimeOffset jetzt)
        => _schutz.Protect(Guid.NewGuid().ToString("N") + "|" + jetzt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

    /// <summary>Prüft die Marke und liefert die Kennung des Einsatzes und die Sekunden seit dem Start.</summary>
    public bool MarkePruefen(string? marke, DateTimeOffset jetzt, out Guid spielId, out double verstricheneSekunden)
    {
        spielId = Guid.Empty;
        verstricheneSekunden = 0;
        if (string.IsNullOrEmpty(marke) || marke.Length > 500)
        {
            return false;
        }

        string klartext;
        try
        {
            klartext = _schutz.Unprotect(marke);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }

        var teile = klartext.Split('|');
        if (teile.Length != 2
            || !Guid.TryParseExact(teile[0], "N", out var id)
            || !long.TryParse(teile[1], NumberStyles.None, CultureInfo.InvariantCulture, out var start))
        {
            return false;
        }

        double verstrichen;
        try
        {
            verstrichen = (jetzt - DateTimeOffset.FromUnixTimeSeconds(start)).TotalSeconds;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if (verstrichen < 0 || verstrichen > MarkeGueltig.TotalSeconds)
        {
            return false;
        }

        spielId = id;
        verstricheneSekunden = verstrichen;
        return true;
    }

    // --- Prüfungen ---

    /// <summary>
    /// Bereinigt den Namen (Leerzeichen zusammenfassen) und prüft ihn. Gibt null zurück, wenn er nicht erlaubt ist:
    /// zu kurz oder zu lang, oder mit anderen Zeichen als Buchstaben, Ziffern, Leerzeichen und <see cref="ErlaubteSonderzeichen"/>.
    /// </summary>
    public static string? NameBereinigen(string? roh)
    {
        if (string.IsNullOrWhiteSpace(roh) || roh.Length > 200)
        {
            return null;
        }

        string text;
        try
        {
            text = roh.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var name = new StringBuilder();
        var letztesWarLeerzeichen = true;
        foreach (var zeichen in text)
        {
            if (char.IsWhiteSpace(zeichen))
            {
                if (!letztesWarLeerzeichen)
                {
                    name.Append(' ');
                }

                letztesWarLeerzeichen = true;
                continue;
            }

            if (!char.IsLetterOrDigit(zeichen) && !ErlaubteSonderzeichen.Contains(zeichen))
            {
                return null;
            }

            name.Append(zeichen);
            letztesWarLeerzeichen = false;
        }

        var ergebnis = name.ToString().TrimEnd();
        return ergebnis.Length >= MinNameLaenge && ergebnis.Length <= MaxNameLaenge ? ergebnis : null;
    }

    /// <summary>Kann ein Einsatz mit dieser Dauer und Punktzahl in der verstrichenen Zeit überhaupt gespielt worden sein?</summary>
    public static bool IstPlausibel(int punkte, int sekunden, double verstricheneSekunden)
        => punkte > 0
           && sekunden >= MinSekunden
           && sekunden <= verstricheneSekunden + 2
           && punkte <= Grundpunkte + MaxPunkteProSekunde * sekunden;

    // --- Datenbank ---

    public async Task<List<Eintrag>> Top10Async(CancellationToken ct = default)
    {
        var zeilen = await _db.LoeschspielErgebnisse.AsNoTracking()
            .OrderByDescending(e => e.Punkte).ThenBy(e => e.Datum).ThenBy(e => e.Id)
            .Take(Plaetze)
            .Select(e => new { e.Name, e.Punkte, e.Sekunden })
            .ToListAsync(ct);

        return zeilen.Select((z, i) => new Eintrag(i + 1, z.Name, z.Punkte, z.Sekunden)).ToList();
    }

    /// <summary>Der Platz, den eine Punktzahl belegen würde, oder null, wenn sie nicht unter die Top 10 kommt. Bei Gleichstand gilt der frühere Eintrag.</summary>
    public async Task<int?> PlatzFuerAsync(int punkte, CancellationToken ct = default)
    {
        if (punkte <= 0)
        {
            return null;
        }

        var davor = await _db.LoeschspielErgebnisse.CountAsync(e => e.Punkte >= punkte, ct);
        return davor < Plaetze ? davor + 1 : null;
    }

    public async Task<Eintragung> EintragenAsync(string? marke, string? rohName, int punkte, int sekunden, CancellationToken ct = default)
    {
        var jetzt = DateTimeOffset.UtcNow;
        if (!MarkePruefen(marke, jetzt, out var spielId, out var verstrichen))
        {
            return new Eintragung(Ausgang.MarkeUngueltig, null);
        }

        var name = NameBereinigen(rohName);
        if (name is null)
        {
            return new Eintragung(Ausgang.NameUngueltig, null);
        }

        if (!IstPlausibel(punkte, sekunden, verstrichen))
        {
            return new Eintragung(Ausgang.Unplausibel, null);
        }

        if (await _db.LoeschspielErgebnisse.AnyAsync(e => e.SpielId == spielId, ct))
        {
            return new Eintragung(Ausgang.BereitsEingetragen, null);
        }

        var platz = await PlatzFuerAsync(punkte, ct);
        if (platz is null)
        {
            return new Eintragung(Ausgang.NichtPlatziert, null);
        }

        _db.LoeschspielErgebnisse.Add(new LoeschspielErgebnis
        {
            SpielId = spielId,
            Name = name,
            Punkte = punkte,
            Sekunden = sekunden,
            Datum = jetzt.UtcDateTime,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Zwei gleichzeitige Anfragen mit derselben Marke: die zweite kommt hier an.
            _db.ChangeTracker.Clear();
            return new Eintragung(Ausgang.BereitsEingetragen, null);
        }

        // Nur die besten Einträge behalten.
        var ueberzaehlige = await _db.LoeschspielErgebnisse
            .OrderByDescending(e => e.Punkte).ThenBy(e => e.Datum).ThenBy(e => e.Id)
            .Skip(MaxGespeichert)
            .ToListAsync(ct);
        if (ueberzaehlige.Count > 0)
        {
            _db.LoeschspielErgebnisse.RemoveRange(ueberzaehlige);
            await _db.SaveChangesAsync(ct);
        }

        return new Eintragung(Ausgang.Eingetragen, platz);
    }
}
