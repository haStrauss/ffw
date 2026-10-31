using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ffw.Pages
{
    public class AnmeldenModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;

        public AnmeldenModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; private set; } = "/fotos";

        public class InputModel
        {
            [Required(ErrorMessage = "Bitte die E-Mail-Adresse eingeben.")]
            [EmailAddress(ErrorMessage = "Bitte eine gültige E-Mail-Adresse eingeben.")]
            public string Email { get; set; } = "";

            [Required(ErrorMessage = "Bitte das Passwort eingeben.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            public bool RememberMe { get; set; }
        }

        public IActionResult OnGet(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return LocalRedirect(SichereRueckkehrUrl(returnUrl));
            }

            ReturnUrl = SichereRueckkehrUrl(returnUrl);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            ReturnUrl = SichereRueckkehrUrl(returnUrl);

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var benutzer = await _userManager.FindByEmailAsync(Input.Email.Trim());
            var ergebnis = benutzer is null
                ? Microsoft.AspNetCore.Identity.SignInResult.Failed
                : await _signInManager.PasswordSignInAsync(benutzer, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (ergebnis.Succeeded)
            {
                return LocalRedirect(ReturnUrl);
            }

            ModelState.AddModelError(string.Empty, ergebnis.IsLockedOut
                ? "Zu viele fehlgeschlagene Versuche. Bitte in 15 Minuten erneut versuchen."
                : "E-Mail-Adresse oder Passwort ist falsch.");
            return Page();
        }

        // Verhindert Weiterleitungen auf fremde Seiten nach dem Anmelden.
        private string SichereRueckkehrUrl(string? returnUrl)
            => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/fotos";
    }
}
