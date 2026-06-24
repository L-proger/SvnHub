using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "AdminUsers")]
public sealed class UserModel : PageModel
{
    private readonly UserService _users;

    public UserModel(UserService users)
    {
        _users = users;
    }

    public PortalUser? TargetUser { get; private set; }

    public bool CanAssignRoles { get; private set; }

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

        var newRoles = PortalUserRoles.None;
        if (RolesInput.Owner) newRoles |= PortalUserRoles.Owner;
        if (RolesInput.AdminUsers) newRoles |= PortalUserRoles.AdminUsers;
        if (RolesInput.AdminRepo) newRoles |= PortalUserRoles.AdminRepo;
        if (RolesInput.AdminSystem) newRoles |= PortalUserRoles.AdminSystem;
        if (RolesInput.AdminHooks) newRoles |= PortalUserRoles.AdminHooks;

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
                AdminRepo = user.Roles.HasFlag(PortalUserRoles.AdminRepo),
                AdminSystem = user.Roles.HasFlag(PortalUserRoles.AdminSystem),
                AdminHooks = user.Roles.HasFlag(PortalUserRoles.AdminHooks),
            };
        }

        var isOwner = User?.IsInRole(nameof(PortalUserRoles.Owner)) ?? false;
        var isUserAdmin = User?.IsInRole(nameof(PortalUserRoles.AdminUsers)) ?? false;

        CanAssignRoles = isOwner;
        CanDelete = isOwner;
        CanChangePassword = isOwner || (isUserAdmin && !user.Roles.HasAnyAdminRole());
        return true;
    }

    public sealed class RolesInputModel
    {
        [Display(Name = "Owner")]
        public bool Owner { get; set; }

        [Display(Name = "AdminUsers")]
        public bool AdminUsers { get; set; }

        [Display(Name = "AdminRepo")]
        public bool AdminRepo { get; set; }

        [Display(Name = "AdminSystem")]
        public bool AdminSystem { get; set; }

        [Display(Name = "AdminHooks")]
        public bool AdminHooks { get; set; }
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
