using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "admin.users")]
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

    public bool CanAssignAdministrativeRoles =>
        User?.IsInRole(PortalUserRoleExtensions.OwnerClaim) ?? false;

    public bool CanAssignRepoRead =>
        User?.IsInRole(PortalUserRoleExtensions.RepoReadClaim) ?? false;

    public bool CanAssignRepoWrite =>
        User?.IsInRole(PortalUserRoleExtensions.RepoWriteClaim) ?? false;

    public bool CanAssignRepoAdmin =>
        User?.IsInRole(PortalUserRoleExtensions.RepoAdminClaim) ?? false;

    public bool CanAssignRepoCreate =>
        User?.IsInRole(PortalUserRoleExtensions.RepoCreateClaim) ?? false;

    public IActionResult OnGet()
    {
        Input.RepoWrite = CanAssignRepoWrite;
        Input.RepoRead = !CanAssignRepoWrite && CanAssignRepoRead;
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
        if (CanAssignRepoRead && Input.RepoRead) roles |= PortalUserRoles.RepoRead;
        if (CanAssignRepoWrite && Input.RepoWrite) roles |= PortalUserRoles.RepoWrite;
        if (CanAssignRepoAdmin && Input.RepoAdmin) roles |= PortalUserRoles.RepoAdmin;
        if (CanAssignRepoCreate && Input.RepoCreate) roles |= PortalUserRoles.RepoCreate;
        if (CanAssignAdministrativeRoles)
        {
            if (Input.Owner) roles |= PortalUserRoles.Owner;
            if (Input.AdminUsers) roles |= PortalUserRoles.AdminUsers;
            if (Input.AdminSystem) roles |= PortalUserRoles.AdminSystem;
            if (Input.RepoHooks) roles |= PortalUserRoles.RepoHooks;
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

        [Display(Name = "repo.admin")]
        public bool RepoAdmin { get; set; }

        [Display(Name = "repo.create")]
        public bool RepoCreate { get; set; }

        [Display(Name = "repo.read")]
        public bool RepoRead { get; set; }

        [Display(Name = "repo.write")]
        public bool RepoWrite { get; set; }

        [Display(Name = "admin.system")]
        public bool AdminSystem { get; set; }

        [Display(Name = "repo.hooks")]
        public bool RepoHooks { get; set; }

        [Display(Name = "admin.users")]
        public bool AdminUsers { get; set; }

        [Display(Name = "owner")]
        public bool Owner { get; set; }
    }
}
