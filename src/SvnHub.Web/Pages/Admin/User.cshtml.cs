using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "Admin")]
public sealed class UserModel : PageModel
{
    private readonly UserService _users;

    public UserModel(UserService users)
    {
        _users = users;
    }

    public PortalUser? TargetUser { get; private set; }

    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? Success { get; set; }

    [BindProperty]
    public RoleInputModel RoleInput { get; set; } = new();

    [BindProperty]
    public PasswordInputModel PasswordInput { get; set; } = new();

    [BindProperty]
    public DeleteInputModel DeleteInput { get; set; } = new();

    public IActionResult OnGet(Guid userId)
    {
        var user = _users.ListUsers().FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return NotFound();
        }

        TargetUser = user;
        RoleInput.Role = user.Role.ToString();
        return Page();
    }

    public async Task<IActionResult> OnPostChangeRoleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _users.ListUsers().FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return NotFound();
        }

        TargetUser = user;
        PasswordInput = new();
        DeleteInput = new();

        ModelState.Clear();
        if (!TryValidateModel(RoleInput, nameof(RoleInput)))
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var newRole = RoleInput.Role switch
        {
            "Admin" => PortalRole.Admin,
            _ => PortalRole.User,
        };

        var result = await _users.ChangeRoleAsync(actorId, userId, newRole, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to change role.";
            return RedirectToPage(new { userId });
        }

        Success = $"Role updated for '{result.Value!.UserName}'.";
        return RedirectToPage(new { userId });
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _users.ListUsers().FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return NotFound();
        }

        TargetUser = user;
        RoleInput = new() { Role = user.Role.ToString() };
        DeleteInput = new();

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
        var user = _users.ListUsers().FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return NotFound();
        }

        TargetUser = user;
        RoleInput = new() { Role = user.Role.ToString() };
        PasswordInput = new();

        ModelState.Clear();
        if (!TryValidateModel(DeleteInput, nameof(DeleteInput)))
        {
            return Page();
        }

        if (!string.Equals(DeleteInput.ConfirmUserName.Trim(), user.UserName, StringComparison.Ordinal))
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

        TempData["Success"] = $"User '{user.UserName}' deleted.";
        return RedirectToPage("/Admin/Users");
    }

    public sealed class RoleInputModel
    {
        [Required]
        public string Role { get; set; } = "User";
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

