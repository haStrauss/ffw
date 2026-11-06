using System.Globalization;
using ffw.Data;
using ffw.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ffw.Pages
{
    [RequestSizeLimit(DokumenteModel.MaxGesamtBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DokumenteModel.MaxGesamtBytes)]
    public class DokumenteModel : PageModel
    {
        public const long MaxGesamtBytes = 600L * 1024 * 1024;
        private const long MaxDateiBytes = 50L * 1024 * 1024;

        private readonly AppDbContext _db;
        private readonly DokumentSpeicher _speicher;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IAuthorizationService _autorisierung;

        public DokumenteModel(AppDbContext db, DokumentSpeicher speicher, SignInManager<IdentityUser> signInManager,
            IAuthorizationService autorisierung)
        {
            _db = db;
            _speicher = speicher;
            _signInManager = signInManager;
            _autorisierung = autorisierung;
        }

        public List<KategorieAnsicht> Kategorien { get; private set; } = new();

        public bool Angemeldet => User.Identity?.IsAuthenticated == true;

        /// <summary>Darf hochladen und löschen (Recht Dokumente.Bearbeiten, nicht nur "angemeldet").</summary>
        public bool KannBearbeiten { get; private set; }

        public string ErlaubteEndungen => string.Join(",", DokumentSpeicher.ErlaubteEndungen);

        [TempData]
        public string? Erfolg { get; set; }

        [TempData]
        public string? Fehler { get; set; }

        public class KategorieAnsicht
        {
            public int Id { get; set; }

            public string Name { get; set; } = "";

            public List<DokumentAnsicht> Dokumente { get; set; } = new();
        }

        public class DokumentAnsicht
        {
            public Guid DateiId { get; set; }

            public string Name { get; set; } = "";

            public long Groesse { get; set; }

            public DateTime HochgeladenAm { get; set; }
        }

        public async Task OnGetAsync()
        {
            KannBearbeiten = await DarfBearbeitenAsync();

            var kategorien = await _db.DokumentKategorien
                .AsNoTracking()
                .OrderBy(k => k.Name)
                .Select(k => new KategorieAnsicht
                {
                    Id = k.Id,
                    Name = k.Name,
                    Dokumente = k.Dokumente
                        .OrderByDescending(d => d.HochgeladenAm)
                        .ThenByDescending(d => d.Id)
                        .Select(d => new DokumentAnsicht
                        {
                            DateiId = d.DateiId,
                            Name = d.Name,
                            Groesse = d.Groesse,
                            HochgeladenAm = d.HochgeladenAm
                        })
                        .ToList()
                })
                .ToListAsync();

            // Oberpunkte ohne Dokumente gibt es nicht (sie werden beim Löschen des letzten Dokuments entfernt).
            Kategorien = kategorien;
        }

        public async Task<IActionResult> OnPostUploadAsync(
            int? kategorieId, string? neueKategorie, List<IFormFile> dateien, CancellationToken ct)
        {
            var abgewiesen = await BearbeitenPruefenAsync();
            if (abgewiesen is not null)
            {
                return abgewiesen;
            }

            // Oberpunkt bestimmen: ein eingegebener neuer Name hat Vorrang vor der Auswahl.
            DokumentKategorie? kategorie;
            var name = neueKategorie?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                if (name.Length > 100)
                {
                    Fehler = "Der Name des Oberpunkts darf höchstens 100 Zeichen lang sein.";
                    return RedirectToPage();
                }

                var kleinbuchstaben = name.ToLower();
                kategorie = await _db.DokumentKategorien.FirstOrDefaultAsync(k => k.Name.ToLower() == kleinbuchstaben, ct)
                            ?? new DokumentKategorie { Name = name };
            }
            else if (kategorieId is int id)
            {
                kategorie = await _db.DokumentKategorien.FirstOrDefaultAsync(k => k.Id == id, ct);
            }
            else
            {
                kategorie = null;
            }

            if (kategorie is null)
            {
                Fehler = "Bitte einen Oberpunkt auswählen oder einen neuen anlegen.";
                return RedirectToPage();
            }

            var ausgewaehlt = dateien.Where(d => d.Length > 0).ToList();
            if (ausgewaehlt.Count == 0)
            {
                Fehler = "Bitte mindestens ein Dokument auswählen.";
                return RedirectToPage();
            }

            var gespeichert = new List<Dokument>();
            var abgelehnt = new List<string>();
            foreach (var datei in ausgewaehlt)
            {
                var anzeigeName = Path.GetFileName(datei.FileName).Trim();
                if (datei.Length > MaxDateiBytes || !NameIstGueltig(anzeigeName))
                {
                    abgelehnt.Add(anzeigeName);
                    continue;
                }

                await using var stream = datei.OpenReadStream();
                var dateiId = await _speicher.SpeichernAsync(stream, anzeigeName, ct);
                if (dateiId is Guid gespeicherteId)
                {
                    gespeichert.Add(new Dokument
                    {
                        Kategorie = kategorie,
                        DateiId = gespeicherteId,
                        Name = anzeigeName,
                        Groesse = datei.Length,
                        HochgeladenAm = DateTime.UtcNow
                    });
                }
                else
                {
                    abgelehnt.Add(anzeigeName);
                }
            }

            if (gespeichert.Count > 0)
            {
                try
                {
                    if (kategorie.Id == 0)
                    {
                        _db.DokumentKategorien.Add(kategorie);
                    }

                    _db.Dokumente.AddRange(gespeichert);
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    foreach (var dokument in gespeichert)
                    {
                        _speicher.Loeschen(dokument.DateiId);
                    }

                    Fehler = "Die Dokumente konnten nicht gespeichert werden. Bitte erneut versuchen.";
                    return RedirectToPage();
                }

                Erfolg = gespeichert.Count == 1
                    ? $"1 Dokument wurde zu „{kategorie.Name}“ hinzugefügt."
                    : $"{gespeichert.Count} Dokumente wurden zu „{kategorie.Name}“ hinzugefügt.";
            }

            if (abgelehnt.Count > 0)
            {
                var beispiele = string.Join(", ", abgelehnt.Take(5)) + (abgelehnt.Count > 5 ? " …" : "");
                Fehler = $"{abgelehnt.Count} Datei(en) wurden nicht übernommen (erlaubt sind PDF, Word, Excel, PowerPoint, OpenDocument, TXT und CSV bis 50 MB): {beispiele}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostLoeschenAsync(Guid dateiId, CancellationToken ct)
        {
            var abgewiesen = await BearbeitenPruefenAsync();
            if (abgewiesen is not null)
            {
                return abgewiesen;
            }

            var dokument = await _db.Dokumente.FirstOrDefaultAsync(d => d.DateiId == dateiId, ct);
            if (dokument is null)
            {
                Fehler = "Das Dokument wurde nicht gefunden. Es wurde vielleicht schon gelöscht.";
                return RedirectToPage();
            }

            // Ein Oberpunkt ohne Dokumente wird automatisch mit entfernt.
            string? entfernterOberpunkt = null;
            try
            {
                _db.Dokumente.Remove(dokument);

                var weitereDokumente = await _db.Dokumente.AnyAsync(d => d.KategorieId == dokument.KategorieId && d.Id != dokument.Id, ct);
                if (!weitereDokumente)
                {
                    var kategorie = await _db.DokumentKategorien.FirstOrDefaultAsync(k => k.Id == dokument.KategorieId, ct);
                    if (kategorie is not null)
                    {
                        _db.DokumentKategorien.Remove(kategorie);
                        entfernterOberpunkt = kategorie.Name;
                    }
                }

                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                Fehler = "Das Dokument konnte nicht gelöscht werden. Bitte erneut versuchen.";
                return RedirectToPage();
            }

            // Erst nach erfolgreichem Datenbank-Eintrag die Datei entfernen.
            _speicher.Loeschen(dateiId);

            Erfolg = entfernterOberpunkt is null
                ? "Das Dokument wurde gelöscht."
                : $"Das Dokument wurde gelöscht. Der leere Oberpunkt „{entfernterOberpunkt}“ wurde ebenfalls entfernt.";
            return RedirectToPage();
        }

        private async Task<bool> DarfBearbeitenAsync()
            => (await _autorisierung.AuthorizeAsync(User, Rechte.DokumenteBearbeiten)).Succeeded;

        /// <summary>Null, wenn der Benutzer bearbeiten darf; sonst die passende Antwort (Anmelden bzw. Hinweis).</summary>
        private async Task<IActionResult?> BearbeitenPruefenAsync()
        {
            if (!Angemeldet)
            {
                return RedirectToPage("Anmelden", new { returnUrl = "/dokumente" });
            }

            if (!await DarfBearbeitenAsync())
            {
                Fehler = "Dafür fehlt die Berechtigung.";
                return RedirectToPage();
            }

            return null;
        }

        public async Task<IActionResult> OnPostLogoutAsync()
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage();
        }

        /// <summary>Dateiendung für das Etikett in der Liste, z. B. "PDF".</summary>
        public static string Endung(string dateiName)
            => Path.GetExtension(dateiName).TrimStart('.').ToUpperInvariant();

        /// <summary>Dateigröße für Menschen, z. B. "340 KB" oder "1,2 MB".</summary>
        public static string Groesse(long bytes)
        {
            var text = bytes switch
            {
                < 1024 => $"{bytes} Byte",
                < 1024 * 1024 => $"{Math.Max(1, (int)Math.Round(bytes / 1024.0))} KB",
                _ => $"{(bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture)} MB"
            };
            return text.Replace('.', ',');
        }

        private static bool NameIstGueltig(string name)
            => name.Length is > 0 and <= 255 && !name.Any(char.IsControl);
    }
}
