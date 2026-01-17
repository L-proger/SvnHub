using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "AdminSystem")]
public sealed class UsersCreateModel : PageModel
{
    private readonly UserService _users;

    public UsersCreateModel(UserService users)
    {
        _users = users;
    }

    [BindProperty]
    public CreateUserInput Input { get; set; } = new();

    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var roles = PortalUserRoles.None;
        if (Input.AdminRepo) roles |= PortalUserRoles.AdminRepo;
        if (Input.AdminSystem) roles |= PortalUserRoles.AdminSystem;
        if (Input.AdminHooks) roles |= PortalUserRoles.AdminHooks;

        if (roles.HasFlag(PortalUserRoles.AdminRepo) || roles.HasFlag(PortalUserRoles.AdminSystem) || roles.HasFlag(PortalUserRoles.AdminHooks))
        {
            // ok
        }

        var result = await _users.CreateUserAsync(actorId, Input.UserName, Input.Password, roles, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to create user.";
            return Page();
        }

        TempData["Success"] = $"User '{result.Value!.UserName}' created.";
        return RedirectToPage("/Admin/Users");
    }

    public sealed class CreateUserInput
    {
        [Required]
        [Display(Name = "User name")]
        public string UserName { get; set; } = "";

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = "";

        [Display(Name = "AdminRepo")]
        public bool AdminRepo { get; set; }

        [Display(Name = "AdminSystem")]
        public bool AdminSystem { get; set; }

        [Display(Name = "AdminHooks")]
        public bool AdminHooks { get; set; }
    }
}
