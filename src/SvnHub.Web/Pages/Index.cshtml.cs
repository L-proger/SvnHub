using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SvnHub.Web.Pages;

public sealed class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        if (User?.Identity?.IsAuthenticated ?? false)
        {
            return RedirectToPage("/Repos/Index");
        }

        return RedirectToPage("/Login");
    }
}
