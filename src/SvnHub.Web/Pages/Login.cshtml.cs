using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages;

public sealed class LoginModel : PageModel
{
    private readonly UserService _users;

    public LoginModel(UserService users)
    {
        _users = users;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated ?? false)
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = _users.Authenticate(Input.UserName, Input.Password);
        if (!result.Success || result.Value is null)
        {
            Error = result.Error ?? "Login failed.";
            return Page();
        }

        await SignInAsync(result.Value);
        return RedirectToPage("/Index");
    }

    private async Task SignInAsync(PortalUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.UserName),
        };

        if (user.Roles.HasFlag(PortalUserRoles.AdminRepo))
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminRepo)));
        }
        if (user.Roles.HasFlag(PortalUserRoles.AdminSystem))
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminSystem)));
        }
        if (user.Roles.HasFlag(PortalUserRoles.AdminHooks))
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminHooks)));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity)
        );
    }

    public sealed class LoginInput
    {
        [Required]
        public string UserName { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";
    }
}
