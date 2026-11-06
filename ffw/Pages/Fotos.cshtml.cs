using ffw.Data;
using ffw.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ffw.Pages
{
    [RequestSizeLimit(FotosModel.MaxGesamtBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = FotosModel.MaxGesamtBytes)]
    public class FotosModel : PageModel
    {
        public const long MaxGesamtBytes = 600L * 1024 * 1024;
        private const long MaxDateiBytes = 40L * 1024 * 1024;

        private readonly AppDbContext _db;
        private readonly FotoSpeicher _speicher;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IAuthorizationService _autorisierung;

        public FotosModel(AppDbContext db, FotoSpeicher speicher, SignInManager<IdentityUser> signInManager,
            IAuthorizationService autorisierung)
        {
            _db = db;
            _speicher = speicher;
            _signInManager = signInManager;
            _autorisierung = autorisierung;
        }

        public List<KategorieAnsicht> Kategorien { get; private set; } = new();

        public bool Angemeldet => User.Identity?.IsAuthenticated == true;

        /// <summary>Darf hochladen und löschen (Recht Fotos.Bearbeiten, nicht nur "angemeldet").</summary>
        public bool KannBearbeiten { get; private set; }

        [TempData]
        public string? Erfolg { get; set; }

        [TempData]
        public string? Fehler { get; set; }

        public class KategorieAnsicht
        {
            public int Id { get; set; }

            public string Name { get; set; } = "";

            public List<Guid> Fotos { get; set; } = new();
        }

        public async Task OnGetAsync()
        {
            KannBearbeiten = await DarfBearbeitenAsync();

            var kategorien = await _db.Kategorien
                .AsNoTracking()
                .OrderBy(k => k.Name)
                .Select(k => new KategorieAnsicht
                {
                    Id = k.Id,
                    Name = k.Name,
                    Fotos = k.Fotos
                        .OrderByDescending(f => f.HochgeladenAm)
                        .ThenByDescending(f => f.Id)
                        .Select(f => f.DateiId)
                        .ToList()
                })
                .ToListAsync();

            // Oberpunkte ohne Fotos gibt es nicht (sie werden beim Löschen des letzten Fotos entfernt).
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
            FotoKategorie? kategorie;
            var name = neueKategorie?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                if (name.Length > 100)
                {
                    Fehler = "Der Name des Oberpunkts darf höchstens 100 Zeichen lang sein.";
                    return RedirectToPage();
                }

                var kleinbuchstaben = name.ToLower();
                kategorie = await _db.Kategorien.FirstOrDefaultAsync(k => k.Name.ToLower() == kleinbuchstaben, ct)
                            ?? new FotoKategorie { Name = name };
            }
            else if (kategorieId is int id)
            {
                kategorie = await _db.Kategorien.FirstOrDefaultAsync(k => k.Id == id, ct);
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
                Fehler = "Bitte mindestens ein Foto auswählen.";
                return RedirectToPage();
            }

            var gespeichert = new List<Guid>();
            var abgelehnt = new List<string>();
            foreach (var datei in ausgewaehlt)
            {
                var anzeigeName = Path.GetFileName(datei.FileName);
                if (datei.Length > MaxDateiBytes)
                {
                    abgelehnt.Add(anzeigeName);
                    continue;
                }

                await using var stream = datei.OpenReadStream();
                var dateiId = await _speicher.SpeichernAsync(stream, ct);
                if (dateiId is Guid gespeicherteId)
                {
                    gespeichert.Add(gespeicherteId);
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
                        _db.Kategorien.Add(kategorie);
                    }

                    foreach (var dateiId in gespeichert)
                    {
                        _db.Fotos.Add(new Foto { Kategorie = kategorie, DateiId = dateiId, HochgeladenAm = DateTime.UtcNow });
                    }

                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    foreach (var dateiId in gespeichert)
                    {
                        _speicher.Loeschen(dateiId);
                    }

                    Fehler = "Die Fotos konnten nicht gespeichert werden. Bitte erneut versuchen.";
                    return RedirectToPage();
                }

                Erfolg = gespeichert.Count == 1
                    ? $"1 Foto wurde zu „{kategorie.Name}“ hinzugefügt."
                    : $"{gespeichert.Count} Fotos wurden zu „{kategorie.Name}“ hinzugefügt.";
            }

            if (abgelehnt.Count > 0)
            {
                var beispiele = string.Join(", ", abgelehnt.Take(5)) + (abgelehnt.Count > 5 ? " …" : "");
                Fehler = $"{abgelehnt.Count} Datei(en) wurden nicht übernommen (erlaubt sind Bilder wie JPG, PNG, WebP oder GIF bis 40 MB): {beispiele}";
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

            var foto = await _db.Fotos.FirstOrDefaultAsync(f => f.DateiId == dateiId, ct);
            if (foto is null)
            {
                Fehler = "Das Foto wurde nicht gefunden. Es wurde vielleicht schon gelöscht.";
                return RedirectToPage();
            }

            // Ein Oberpunkt ohne Fotos wird automatisch mit entfernt.
            string? entfernterOberpunkt = null;
            try
            {
                _db.Fotos.Remove(foto);

                var weitereFotos = await _db.Fotos.AnyAsync(f => f.KategorieId == foto.KategorieId && f.Id != foto.Id, ct);
                if (!weitereFotos)
                {
                    var kategorie = await _db.Kategorien.FirstOrDefaultAsync(k => k.Id == foto.KategorieId, ct);
                    if (kategorie is not null)
                    {
                        _db.Kategorien.Remove(kategorie);
                        entfernterOberpunkt = kategorie.Name;
                    }
                }

                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                Fehler = "Das Foto konnte nicht gelöscht werden. Bitte erneut versuchen.";
                return RedirectToPage();
            }

            // Erst nach erfolgreichem Datenbank-Eintrag die Dateien entfernen.
            _speicher.Loeschen(dateiId);

            Erfolg = entfernterOberpunkt is null
                ? "Das Foto wurde gelöscht."
                : $"Das Foto wurde gelöscht. Der leere Oberpunkt „{entfernterOberpunkt}“ wurde ebenfalls entfernt.";
            return RedirectToPage();
        }

        private async Task<bool> DarfBearbeitenAsync()
            => (await _autorisierung.AuthorizeAsync(User, Rechte.FotosBearbeiten)).Succeeded;

        /// <summary>Null, wenn der Benutzer bearbeiten darf; sonst die passende Antwort (Anmelden bzw. Hinweis).</summary>
        private async Task<IActionResult?> BearbeitenPruefenAsync()
        {
            if (!Angemeldet)
            {
                return RedirectToPage("Anmelden", new { returnUrl = "/fotos" });
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
    }
}
