using ffw.Data;
using ffw.Services;
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

        public FotosModel(AppDbContext db, FotoSpeicher speicher, SignInManager<IdentityUser> signInManager)
        {
            _db = db;
            _speicher = speicher;
            _signInManager = signInManager;
        }

        public List<KategorieAnsicht> Kategorien { get; private set; } = new();

        public bool Angemeldet => User.Identity?.IsAuthenticated == true;

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

            // Besucher sehen nur Oberpunkte mit Fotos, angemeldete Benutzer alle (zum Auswählen beim Hochladen).
            Kategorien = Angemeldet ? kategorien : kategorien.Where(k => k.Fotos.Count > 0).ToList();
        }

        public async Task<IActionResult> OnPostUploadAsync(
            int? kategorieId, string? neueKategorie, List<IFormFile> dateien, CancellationToken ct)
        {
            if (!Angemeldet)
            {
                return RedirectToPage("Anmelden", new { returnUrl = "/fotos" });
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

        public async Task<IActionResult> OnPostLogoutAsync()
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage();
        }
    }
}
