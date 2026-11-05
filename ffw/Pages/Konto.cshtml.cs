using System.ComponentModel.DataAnnotations;
using ffw.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ffw.Pages
{
    /// <summary>Das eigene Konto: Gruppe ansehen, Passwort ändern, Stand der Zwei-Faktor-Anmeldung.</summary>
    [Authorize]
    public class KontoModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<KontoModel> _logger;

        public KontoModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager,
            ILogger<KontoModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }

        public string Email { get; private set; } = "";

        public string Gruppe { get; private set; } = "";

        public bool ZweiFaktorAktiv { get; private set; }

        [TempData]
        public string? Erfolg { get; set; }

        [BindProperty]
        public PasswortInput Input { get; set; } = new();

        public class PasswortInput
        {
            [Required(ErrorMessage = "Bitte das aktuelle Passwort eingeben.")]
            [DataType(DataType.Password)]
            public string AktuellesPasswort { get; set; } = "";

            [Required(ErrorMessage = "Bitte ein neues Passwort eingeben.")]
            [DataType(DataType.Password)]
            public string NeuesPasswort { get; set; } = "";

            [Required(ErrorMessage = "Bitte das neue Passwort wiederholen.")]
            [DataType(DataType.Password)]
            [Compare(nameof(NeuesPasswort), ErrorMessage = "Die beiden neuen Passwörter stimmen nicht überein.")]
            public string Wiederholung { get; set; } = "";
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var benutzer = await _userManager.GetUserAsync(User);
            if (benutzer is null)
            {
                return await AbmeldenUndAnmeldenAsync();
            }

            await LadenAsync(benutzer);
            return Page();
        }

        public async Task<IActionResult> OnPostPasswortAsync()
        {
            var benutzer = await _userManager.GetUserAsync(User);
            if (benutzer is null)
            {
                return await AbmeldenUndAnmeldenAsync();
            }

            await LadenAsync(benutzer);
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var ergebnis = await _userManager.ChangePasswordAsync(benutzer, Input.AktuellesPasswort, Input.NeuesPasswort);
            if (!ergebnis.Succeeded)
            {
                foreach (var fehler in ergebnis.Errors)
                {
                    ModelState.AddModelError(string.Empty, fehler.Description);
                }

                return Page();
            }

            // Das neue Passwort erneuert den Security Stamp und macht alle Login-Cookies ungültig, auch das eigene.
            // Deshalb wird dieses Gerät sofort neu angemeldet; alle anderen Geräte müssen sich neu anmelden.
            await _signInManager.RefreshSignInAsync(benutzer);
            _logger.LogInformation("Passwort geändert: {Email}", benutzer.Email);

            Erfolg = "Das Passwort wurde geändert. Auf anderen Geräten ist eine neue Anmeldung nötig.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostLogoutAsync()
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage("Anmelden");
        }

        /// <summary>
        /// Der Benutzer wurde gelöscht, sein Login-Cookie gilt aber noch bis zur nächsten Prüfung. Ohne Abmelden würde die
        /// Anmeldeseite den (scheinbar angemeldeten) Besucher gleich wieder hierher zurückschicken.
        /// </summary>
        private async Task<IActionResult> AbmeldenUndAnmeldenAsync()
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage("Anmelden", new { returnUrl = "/konto" });
        }

        private async Task LadenAsync(IdentityUser benutzer)
        {
            Email = benutzer.Email ?? benutzer.UserName ?? "";
            ZweiFaktorAktiv = await _userManager.GetTwoFactorEnabledAsync(benutzer);

            var rollen = await _userManager.GetRolesAsync(benutzer);
            Gruppe = rollen.Count == 0
                ? "keine"
                : string.Join(", ", rollen.OrderBy(r => Rollen.Alle.ToList().IndexOf(r)));
        }
    }
}
