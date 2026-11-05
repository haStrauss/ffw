using System.ComponentModel.DataAnnotations;
using ffw.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ffw.Pages
{
    /// <summary>
    /// Benutzerverwaltung (nur mit dem Recht Benutzer.Verwalten): anlegen, Gruppe ändern, sperren, Passwort setzen, löschen.
    /// Der letzte aktive Admin kann weder gelöscht noch gesperrt noch herabgestuft werden, und das eigene Konto ist hier
    /// gegen versehentliche Änderungen gesperrt.
    /// </summary>
    [Authorize(Policy = Rechte.BenutzerVerwalten)]
    public class BenutzerModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AppDbContext _db;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<BenutzerModel> _logger;

        public BenutzerModel(UserManager<IdentityUser> userManager, AppDbContext db,
            SignInManager<IdentityUser> signInManager, ILogger<BenutzerModel> logger)
        {
            _userManager = userManager;
            _db = db;
            _signInManager = signInManager;
            _logger = logger;
        }

        public List<BenutzerAnsicht> Benutzer { get; private set; } = new();

        public IReadOnlyList<string> Gruppen => Rollen.Alle;

        [TempData]
        public string? Erfolg { get; set; }

        [TempData]
        public string? Fehler { get; set; }

        /// <summary>Nach einem Fehler beim Anlegen bleiben die Eingaben erhalten (außer dem Passwort).</summary>
        [TempData]
        public string? NeueEmail { get; set; }

        [TempData]
        public string? NeueGruppe { get; set; }

        public class BenutzerAnsicht
        {
            public string Id { get; set; } = "";

            public string Email { get; set; } = "";

            public string Gruppe { get; set; } = "";

            public bool Gesperrt { get; set; }

            /// <summary>Dauerhaft gesperrt (vom Admin) im Gegensatz zur automatischen Sperre nach zu vielen Fehlversuchen.</summary>
            public bool DauerhaftGesperrt { get; set; }

            public bool ZweiFaktor { get; set; }

            public bool IstIch { get; set; }
        }

        private string EigeneId => _userManager.GetUserId(User) ?? "";

        public async Task OnGetAsync()
        {
            var benutzer = await _userManager.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
            var zuordnung = await (from ur in _db.UserRoles
                                   join r in _db.Roles on ur.RoleId equals r.Id
                                   select new { ur.UserId, r.Name })
                .ToListAsync();

            var jetzt = DateTimeOffset.UtcNow;
            Benutzer = benutzer.Select(u => new BenutzerAnsicht
            {
                Id = u.Id,
                Email = u.Email ?? u.UserName ?? "",
                Gruppe = zuordnung
                    .Where(z => z.UserId == u.Id && z.Name is not null)
                    .Select(z => z.Name!)
                    .OrderBy(n => Rollen.Alle.ToList().IndexOf(n))
                    .FirstOrDefault() ?? "",
                Gesperrt = u.LockoutEnd > jetzt,
                DauerhaftGesperrt = u.LockoutEnd > jetzt.AddYears(50),
                ZweiFaktor = u.TwoFactorEnabled,
                IstIch = u.Id == EigeneId
            }).ToList();
        }

        public async Task<IActionResult> OnPostAnlegenAsync(string? email, string? passwort, string? gruppe)
        {
            email = email?.Trim();

            if (string.IsNullOrEmpty(email) || !new EmailAddressAttribute().IsValid(email))
            {
                return AnlegenAbgebrochen("Bitte eine gültige E-Mail-Adresse eingeben.", email, gruppe);
            }

            if (string.IsNullOrEmpty(passwort))
            {
                return AnlegenAbgebrochen("Bitte ein Startpasswort eingeben.", email, gruppe);
            }

            if (gruppe is null || !Rollen.Alle.Contains(gruppe))
            {
                return AnlegenAbgebrochen("Bitte eine Gruppe auswählen.", email, gruppe);
            }

            var benutzer = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var ergebnis = await _userManager.CreateAsync(benutzer, passwort);
            if (!ergebnis.Succeeded)
            {
                return AnlegenAbgebrochen(Fehlertext(ergebnis), email, gruppe);
            }

            var zuweisung = await _userManager.AddToRoleAsync(benutzer, gruppe);
            if (!zuweisung.Succeeded)
            {
                // Ein Benutzer ohne Gruppe soll nicht übrig bleiben.
                await _userManager.DeleteAsync(benutzer);
                return AnlegenAbgebrochen(Fehlertext(zuweisung), email, gruppe);
            }

            _logger.LogInformation("{Admin} hat Benutzer {Email} in der Gruppe {Gruppe} angelegt.", User.Identity?.Name, email, gruppe);
            Erfolg = $"Benutzer {email} wurde angelegt. Das Startpasswort bitte persönlich weitergeben.";
            return RedirectToPage();
        }

        /// <summary>Meldet den Fehler und behält E-Mail und Gruppe im Formular (das Passwort nicht).</summary>
        private IActionResult AnlegenAbgebrochen(string fehler, string? email, string? gruppe)
        {
            Fehler = fehler;
            NeueEmail = email ?? "";
            NeueGruppe = gruppe ?? "";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostGruppeAsync(string? id, string? gruppe)
        {
            var ziel = await ZielAsync(id);
            if (ziel is null)
            {
                return RedirectToPage();
            }

            if (gruppe is null || !Rollen.Alle.Contains(gruppe))
            {
                Fehler = "Bitte eine Gruppe auswählen.";
                return RedirectToPage();
            }

            var aktuelle = await _userManager.GetRolesAsync(ziel);
            if (aktuelle.Count == 1 && aktuelle[0] == gruppe)
            {
                Erfolg = $"{ziel.Email} gehört bereits zur Gruppe {gruppe}.";
                return RedirectToPage();
            }

            if (gruppe != Rollen.Admin && await WaereLetzterAdminAsync(ziel))
            {
                Fehler = "Der letzte aktive Admin kann nicht herabgestuft werden. Zuerst einen weiteren Admin anlegen.";
                return RedirectToPage();
            }

            // Erst die neue Gruppe zuweisen, dann die alten entfernen: so bleibt der Benutzer bei einem Fehler nie ohne Gruppe.
            if (!aktuelle.Contains(gruppe))
            {
                var zuweisung = await _userManager.AddToRoleAsync(ziel, gruppe);
                if (!zuweisung.Succeeded)
                {
                    Fehler = Fehlertext(zuweisung);
                    return RedirectToPage();
                }
            }

            var alte = aktuelle.Where(r => r != gruppe).ToList();
            if (alte.Count > 0)
            {
                var entfernt = await _userManager.RemoveFromRolesAsync(ziel, alte);
                if (!entfernt.Succeeded)
                {
                    Fehler = Fehlertext(entfernt);
                    return RedirectToPage();
                }
            }

            // Macht das Login-Cookie ungültig, damit die neuen Rechte sofort gelten.
            await _userManager.UpdateSecurityStampAsync(ziel);
            _logger.LogInformation("{Admin} hat Benutzer {Email} in die Gruppe {Gruppe} verschoben.", User.Identity?.Name, ziel.Email, gruppe);
            Erfolg = $"{ziel.Email} gehört jetzt zur Gruppe {gruppe}.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSperrenAsync(string? id, bool sperren)
        {
            var ziel = await ZielAsync(id);
            if (ziel is null)
            {
                return RedirectToPage();
            }

            if (sperren)
            {
                if (await WaereLetzterAdminAsync(ziel))
                {
                    Fehler = "Der letzte aktive Admin kann nicht gesperrt werden. Zuerst einen weiteren Admin anlegen.";
                    return RedirectToPage();
                }

                await _userManager.SetLockoutEnabledAsync(ziel, true);
                var ergebnis = await _userManager.SetLockoutEndDateAsync(ziel, DateTimeOffset.UtcNow.AddYears(100));
                if (!ergebnis.Succeeded)
                {
                    Fehler = Fehlertext(ergebnis);
                    return RedirectToPage();
                }

                // Beendet auch schon angemeldete Sitzungen.
                await _userManager.UpdateSecurityStampAsync(ziel);
                _logger.LogInformation("{Admin} hat Benutzer {Email} gesperrt.", User.Identity?.Name, ziel.Email);
                Erfolg = $"{ziel.Email} wurde gesperrt.";
            }
            else
            {
                var ergebnis = await _userManager.SetLockoutEndDateAsync(ziel, null);
                if (!ergebnis.Succeeded)
                {
                    Fehler = Fehlertext(ergebnis);
                    return RedirectToPage();
                }

                await _userManager.ResetAccessFailedCountAsync(ziel);
                _logger.LogInformation("{Admin} hat Benutzer {Email} entsperrt.", User.Identity?.Name, ziel.Email);
                Erfolg = $"{ziel.Email} wurde entsperrt.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostPasswortAsync(string? id, string? neuesPasswort)
        {
            var ziel = await ZielAsync(id);
            if (ziel is null)
            {
                return RedirectToPage();
            }

            if (string.IsNullOrEmpty(neuesPasswort))
            {
                Fehler = "Bitte ein neues Passwort eingeben.";
                return RedirectToPage();
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(ziel);
            var ergebnis = await _userManager.ResetPasswordAsync(ziel, token, neuesPasswort);
            if (!ergebnis.Succeeded)
            {
                Fehler = Fehlertext(ergebnis);
                return RedirectToPage();
            }

            _logger.LogInformation("{Admin} hat das Passwort von {Email} zurückgesetzt.", User.Identity?.Name, ziel.Email);
            Erfolg = $"Das Passwort von {ziel.Email} wurde gesetzt. Es bitte persönlich weitergeben. Bestehende Anmeldungen wurden beendet.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostZweiFaktorAsync(string? id)
        {
            var ziel = await ZielAsync(id);
            if (ziel is null)
            {
                return RedirectToPage();
            }

            // Notausgang, falls jemand sein Handy verliert und keinen Wiederherstellungscode mehr hat.
            await _userManager.SetTwoFactorEnabledAsync(ziel, false);
            await _userManager.ResetAuthenticatorKeyAsync(ziel);
            _logger.LogInformation("{Admin} hat die Zwei-Faktor-Anmeldung von {Email} zurückgesetzt.", User.Identity?.Name, ziel.Email);
            Erfolg = $"Die Zwei-Faktor-Anmeldung von {ziel.Email} wurde zurückgesetzt.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostLoeschenAsync(string? id)
        {
            var ziel = await ZielAsync(id);
            if (ziel is null)
            {
                return RedirectToPage();
            }

            if (await WaereLetzterAdminAsync(ziel))
            {
                Fehler = "Der letzte aktive Admin kann nicht gelöscht werden. Zuerst einen weiteren Admin anlegen.";
                return RedirectToPage();
            }

            var ergebnis = await _userManager.DeleteAsync(ziel);
            if (!ergebnis.Succeeded)
            {
                Fehler = Fehlertext(ergebnis);
                return RedirectToPage();
            }

            _logger.LogInformation("{Admin} hat Benutzer {Email} gelöscht.", User.Identity?.Name, ziel.Email);
            Erfolg = $"Benutzer {ziel.Email} wurde gelöscht.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostLogoutAsync()
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage("Anmelden");
        }

        /// <summary>Der Benutzer, auf den sich die Aktion bezieht. Das eigene Konto ist ausgenommen.</summary>
        private async Task<IdentityUser?> ZielAsync(string? id)
        {
            var ziel = string.IsNullOrEmpty(id) ? null : await _userManager.FindByIdAsync(id);
            if (ziel is null)
            {
                Fehler = "Der Benutzer wurde nicht gefunden. Er wurde vielleicht schon gelöscht.";
                return null;
            }

            if (ziel.Id == EigeneId)
            {
                Fehler = "Das eigene Konto kann hier nicht geändert werden. Das Passwort ändert man unter „Konto“.";
                return null;
            }

            return ziel;
        }

        private static bool IstAktiv(IdentityUser benutzer)
            => benutzer.LockoutEnd is null || benutzer.LockoutEnd <= DateTimeOffset.UtcNow;

        /// <summary>Wahr, wenn der Benutzer der einzige aktive (nicht gesperrte) Admin ist.</summary>
        private async Task<bool> WaereLetzterAdminAsync(IdentityUser benutzer)
        {
            if (!IstAktiv(benutzer) || !await _userManager.IsInRoleAsync(benutzer, Rollen.Admin))
            {
                return false;
            }

            var admins = await _userManager.GetUsersInRoleAsync(Rollen.Admin);
            return admins.Count(IstAktiv) <= 1;
        }

        private static string Fehlertext(IdentityResult ergebnis)
            => string.Join(" ", ergebnis.Errors.Select(e => e.Description).Distinct());
    }
}
