using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ffw.Pages
{
    /// <summary>Anmeldung mit einem einmaligen Wiederherstellungscode, falls die Authenticator-App nicht verfügbar ist.</summary>
    public class WiederherstellungModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<WiederherstellungModel> _logger;

        public WiederherstellungModel(SignInManager<IdentityUser> signInManager, ILogger<WiederherstellungModel> logger)
        {
            _signInManager = signInManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; private set; } = "/fotos";

        public class InputModel
        {
            [Required(ErrorMessage = "Bitte den Wiederherstellungscode eingeben.")]
            public string Code { get; set; } = "";
        }

        public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
        {
            if (await _signInManager.GetTwoFactorAuthenticationUserAsync() is null)
            {
                return RedirectToPage("Anmelden");
            }

            ReturnUrl = SichereRueckkehrUrl(returnUrl);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            ReturnUrl = SichereRueckkehrUrl(returnUrl);

            var benutzer = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (benutzer is null)
            {
                return RedirectToPage("Anmelden");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var code = Input.Code.Replace(" ", string.Empty);
            var ergebnis = await _signInManager.TwoFactorRecoveryCodeSignInAsync(code);

            if (ergebnis.Succeeded)
            {
                _logger.LogInformation("Anmeldung mit Wiederherstellungscode erfolgreich: {Email}", benutzer.Email);
                return LocalRedirect(ReturnUrl);
            }

            _logger.LogWarning("Wiederherstellungscode falsch für {Email} (gesperrt: {Gesperrt})", benutzer.Email, ergebnis.IsLockedOut);
            ModelState.AddModelError(string.Empty, ergebnis.IsLockedOut
                ? "Das Konto ist gesperrt, zum Beispiel nach zu vielen fehlgeschlagenen Versuchen. Bitte später erneut versuchen oder an einen Administrator wenden."
                : "Der Wiederherstellungscode ist falsch.");
            return Page();
        }

        private string SichereRueckkehrUrl(string? returnUrl)
            => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/fotos";
    }
}
