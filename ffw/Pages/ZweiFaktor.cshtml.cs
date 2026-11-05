using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ffw.Pages
{
    /// <summary>
    /// Zweiter Schritt der Anmeldung, wenn für den Benutzer die Zwei-Faktor-Anmeldung aktiv ist.
    /// Solange niemand sie eingerichtet hat, wird diese Seite nie erreicht.
    /// </summary>
    public class ZweiFaktorModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<ZweiFaktorModel> _logger;

        public ZweiFaktorModel(SignInManager<IdentityUser> signInManager, ILogger<ZweiFaktorModel> logger)
        {
            _signInManager = signInManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; private set; } = "/fotos";

        public bool RememberMe { get; private set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Bitte den Code eingeben.")]
            public string Code { get; set; } = "";
        }

        public async Task<IActionResult> OnGetAsync(string? returnUrl = null, bool rememberMe = false)
        {
            if (await _signInManager.GetTwoFactorAuthenticationUserAsync() is null)
            {
                return RedirectToPage("Anmelden");
            }

            ReturnUrl = SichereRueckkehrUrl(returnUrl);
            RememberMe = rememberMe;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null, bool rememberMe = false)
        {
            ReturnUrl = SichereRueckkehrUrl(returnUrl);
            RememberMe = rememberMe;

            var benutzer = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (benutzer is null)
            {
                return RedirectToPage("Anmelden");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Leerzeichen und Bindestriche, wie sie Authenticator-Apps beim Anzeigen einfügen, werden ignoriert.
            var code = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var ergebnis = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, rememberMe, rememberClient: false);

            if (ergebnis.Succeeded)
            {
                _logger.LogInformation("Anmeldung mit Zwei-Faktor-Code erfolgreich: {Email}", benutzer.Email);
                return LocalRedirect(ReturnUrl);
            }

            _logger.LogWarning("Zwei-Faktor-Code falsch für {Email} (gesperrt: {Gesperrt})", benutzer.Email, ergebnis.IsLockedOut);
            ModelState.AddModelError(string.Empty, ergebnis.IsLockedOut
                ? "Das Konto ist gesperrt, zum Beispiel nach zu vielen fehlgeschlagenen Versuchen. Bitte später erneut versuchen oder an einen Administrator wenden."
                : "Der Code ist falsch.");
            return Page();
        }

        private string SichereRueckkehrUrl(string? returnUrl)
            => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/fotos";
    }
}
