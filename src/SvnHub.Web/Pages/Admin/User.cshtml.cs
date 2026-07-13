using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "admin.users")]
public sealed class UserModel : PageModel
{
    private readonly UserService _users;

    public UserModel(UserService users)
    {
        _users = users;
    }

    public PortalUser? TargetUser { get; private set; }

    public bool CanAssignRoles { get; private set; }

    public bool CanAssignAdministrativeRoles { get; private set; }

    public bool CanAssignRepoRead { get; private set; }

    public bool CanAssignRepoWrite { get; private set; }

    public bool CanAssignRepoAdmin { get; private set; }

    public bool CanAssignRepoCreate { get; private set; }

    public bool CanChangePassword { get; private set; }

    public bool CanDelete { get; private set; }

    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? Success { get; set; }

    [BindProperty]
    public RolesInputModel RolesInput { get; set; } = new();

    [BindProperty]
    public PasswordInputModel PasswordInput { get; set; } = new();

    [BindProperty]
    public DeleteInputModel DeleteInput { get; set; } = new();

    public IActionResult OnGet(Guid userId)
    {
        if (!LoadTargetUser(userId))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostChangeRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!LoadTargetUser(userId, populateRolesInput: false))
        {
            return NotFound();
        }

        PasswordInput = new();
        DeleteInput = new();

        if (!CanAssignRoles)
        {
            return Forbid();
        }

        ModelState.Clear();
        if (!TryValidateModel(RolesInput, nameof(RolesInput)))
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var newRoles = TargetUser!.Roles;
        SetRole(ref newRoles, PortalUserRoles.RepoRead, RolesInput.RepoRead, CanAssignRepoRead);
        SetRole(ref newRoles, PortalUserRoles.RepoWrite, RolesInput.RepoWrite, CanAssignRepoWrite);
        SetRole(ref newRoles, PortalUserRoles.RepoAdmin, RolesInput.RepoAdmin, CanAssignRepoAdmin);
        SetRole(ref newRoles, PortalUserRoles.RepoCreate, RolesInput.RepoCreate, CanAssignRepoCreate);
        SetRole(ref newRoles, PortalUserRoles.Owner, RolesInput.Owner, CanAssignAdministrativeRoles);
        SetRole(ref newRoles, PortalUserRoles.AdminUsers, RolesInput.AdminUsers, CanAssignAdministrativeRoles);
        SetRole(ref newRoles, PortalUserRoles.AdminSystem, RolesInput.AdminSystem, CanAssignAdministrativeRoles);
        SetRole(ref newRoles, PortalUserRoles.RepoHooks, RolesInput.RepoHooks, CanAssignAdministrativeRoles);

        var result = await _users.ChangeRolesAsync(actorId, userId, newRoles, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to change role.";
            return RedirectToPage(new { userId });
        }

        Success = $"Roles updated for '{result.Value!.UserName}'.";
        return RedirectToPage(new { userId });
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!LoadTargetUser(userId))
        {
            return NotFound();
        }

        DeleteInput = new();

        if (!CanChangePassword)
        {
            return Forbid();
        }

        ModelState.Clear();
        if (!TryValidateModel(PasswordInput, nameof(PasswordInput)))
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var result = await _users.ChangePasswordAsync(actorId, userId, PasswordInput.NewPassword, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to change password.";
            return Page();
        }

        Success = $"Password updated for '{result.Value!.UserName}'.";
        return RedirectToPage(new { userId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!LoadTargetUser(userId))
        {
            return NotFound();
        }

        PasswordInput = new();

        if (!CanDelete)
        {
            return Forbid();
        }

        ModelState.Clear();
        if (!TryValidateModel(DeleteInput, nameof(DeleteInput)))
        {
            return Page();
        }

        if (!string.Equals(DeleteInput.ConfirmUserName.Trim(), TargetUser!.UserName, StringComparison.Ordinal))
        {
            ModelState.AddModelError(
                $"{nameof(DeleteInput)}.{nameof(DeleteInputModel.ConfirmUserName)}",
                "Confirmation name does not match.");
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var result = await _users.DeleteUserAsync(actorId, userId, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to delete user.";
            return Page();
        }

        TempData["Success"] = $"User '{TargetUser.UserName}' deleted.";
        return RedirectToPage("/Admin/Users");
    }

    private bool LoadTargetUser(Guid userId, bool populateRolesInput = true)
    {
        var users = _users.ListUsers();
        var user = users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return false;
        }

        TargetUser = user;
        if (populateRolesInput)
        {
            RolesInput = new()
            {
                Owner = user.Roles.HasFlag(PortalUserRoles.Owner),
                AdminUsers = user.Roles.HasFlag(PortalUserRoles.AdminUsers),
                RepoAdmin = user.Roles.HasFlag(PortalUserRoles.RepoAdmin),
                RepoCreate = user.Roles.HasFlag(PortalUserRoles.RepoCreate),
                RepoRead = user.Roles.HasFlag(PortalUserRoles.RepoRead),
                RepoWrite = user.Roles.HasFlag(PortalUserRoles.RepoWrite),
                AdminSystem = user.Roles.HasFlag(PortalUserRoles.AdminSystem),
                RepoHooks = user.Roles.HasFlag(PortalUserRoles.RepoHooks),
            };
        }

        var isOwner = User?.IsInRole(PortalUserRoleExtensions.OwnerClaim) ?? false;
        var isUserAdmin = User?.IsInRole(PortalUserRoleExtensions.AdminUsersClaim) ?? false;
        var targetIsOwner = user.Roles.HasFlag(PortalUserRoles.Owner);
        var canDelegateRepositoryGrants = isUserAdmin && !targetIsOwner;
        var isSelf = Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) &&
            actorId == user.Id;

        CanAssignAdministrativeRoles = isOwner;
        CanAssignRepoRead = isOwner ||
            (canDelegateRepositoryGrants && (User?.IsInRole(PortalUserRoleExtensions.RepoReadClaim) ?? false));
        CanAssignRepoWrite = isOwner ||
            (canDelegateRepositoryGrants && (User?.IsInRole(PortalUserRoleExtensions.RepoWriteClaim) ?? false));
        CanAssignRepoAdmin = isOwner ||
            (canDelegateRepositoryGrants && (User?.IsInRole(PortalUserRoleExtensions.RepoAdminClaim) ?? false));
        CanAssignRepoCreate = isOwner ||
            (canDelegateRepositoryGrants && (User?.IsInRole(PortalUserRoleExtensions.RepoCreateClaim) ?? false));
        CanAssignRoles = CanAssignAdministrativeRoles || CanAssignRepoRead || CanAssignRepoWrite ||
            CanAssignRepoAdmin || CanAssignRepoCreate;
        CanDelete = !isSelf && (isOwner || (isUserAdmin && !user.Roles.HasFlag(PortalUserRoles.Owner)));
        CanChangePassword = isOwner || (isUserAdmin && !user.Roles.HasFlag(PortalUserRoles.Owner));
        return true;
    }

    private static void SetRole(
        ref PortalUserRoles roles,
        PortalUserRoles role,
        bool enabled,
        bool canAssign)
    {
        if (!canAssign)
        {
            return;
        }

        roles = enabled ? roles | role : roles & ~role;
    }

    public sealed class RolesInputModel
    {
        [Display(Name = "owner")]
        public bool Owner { get; set; }

        [Display(Name = "admin.users")]
        public bool AdminUsers { get; set; }

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
    }

    public sealed class PasswordInputModel
    {
        [Required]
        [MinLength(8)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = "";
    }

    public sealed class DeleteInputModel
    {
        [Required]
        [Display(Name = "Confirm user name")]
        public string ConfirmUserName { get; set; } = "";
    }
}
