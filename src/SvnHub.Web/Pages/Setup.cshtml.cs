using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages;

public sealed class SetupModel : PageModel
{
    private readonly SetupService _setup;

    public SetupModel(SetupService setup)
    {
        _setup = setup;
    }

    [BindProperty]
    public SetupInput Input { get; set; } = new();

    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        if (!_setup.IsSetupRequired())
        {
            return RedirectToPage("/Login");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_setup.IsSetupRequired())
        {
            return RedirectToPage("/Login");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!string.Equals(Input.Password, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            Error = "Passwords do not match.";
            return Page();
        }

        var result = await _setup.CreateInitialAdminAsync(Input.UserName, Input.Password, cancellationToken);
        if (!result.Success || result.Value is null)
        {
            Error = result.Error ?? "Setup failed.";
            return Page();
        }

        await SignInAsync(result.Value);
        return RedirectToPage("/Index");
    }

    private async Task SignInAsync(PortalUser user)
    {
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SvnHubClaims.CreatePrincipal(user, CookieAuthenticationDefaults.AuthenticationScheme)
        );
    }

    public sealed class SetupInput
    {
        [Required]
        [Display(Name = "Admin user name")]
        public string UserName { get; set; } = "admin";

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = "";

        [Required]
        [MinLength(8)]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = "";
    }
}
