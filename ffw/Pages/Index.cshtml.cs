using ffw.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ffw.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IAuthorizationService _autorisierung;
        private readonly SignInManager<IdentityUser> _signInManager;

        public IndexModel(IAuthorizationService autorisierung, SignInManager<IdentityUser> signInManager)
        {
            _autorisierung = autorisierung;
            _signInManager = signInManager;
        }

        public bool Angemeldet => User.Identity?.IsAuthenticated == true;

        // Das Menü zeigt nur Seiten, die der Benutzer auch öffnen darf.
        public bool KannFotosSehen { get; private set; }

        public bool KannDokumenteSehen { get; private set; }

        public bool KannBenutzerVerwalten { get; private set; }

        public async Task OnGetAsync()
        {
            if (!Angemeldet)
            {
                return;
            }

            KannFotosSehen = (await _autorisierung.AuthorizeAsync(User, Rechte.FotosLesen)).Succeeded;
            KannDokumenteSehen = (await _autorisierung.AuthorizeAsync(User, Rechte.DokumenteLesen)).Succeeded;
            KannBenutzerVerwalten = (await _autorisierung.AuthorizeAsync(User, Rechte.BenutzerVerwalten)).Succeeded;
        }

        public async Task<IActionResult> OnPostLogoutAsync()
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage();
        }
    }
}
