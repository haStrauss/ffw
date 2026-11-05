using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ffw.Pages
{
    public class KeinZugriffModel : PageModel
    {
        public IActionResult OnGet()
        {
            // Nicht angemeldet: dann liegt es nicht an fehlenden Rechten, sondern am fehlenden Login.
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToPage("Anmelden");
            }

            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Page();
        }
    }
}
