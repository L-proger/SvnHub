using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages;

[Authorize]
public sealed class AccountModel : PageModel
{
    private readonly UserService _users;
    private readonly ApiTokenService _tokens;

    public AccountModel(UserService users, ApiTokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    public PortalUser? CurrentUser { get; private set; }

    public string RolesLabel { get; private set; } = "";

    public string ThemeLabel { get; private set; } = "";

    public IReadOnlyList<ApiToken> Tokens { get; private set; } = [];

    [BindProperty]
    public CreateTokenInput TokenInput { get; set; } = new();

    [BindProperty]
    public ChangePasswordInput PasswordInput { get; set; } = new();

    [TempData]
    public string? CreatedToken { get; set; }

    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? Success { get; set; }

    public IActionResult OnGet()
    {
        if (!LoadAccount(out _))
        {
            return Forbid();
        }

        return Page();
    }

    public IActionResult OnPostCreateToken()
    {
        if (!LoadAccount(out var userId))
        {
            return Forbid();
        }

        ModelState.Clear();
        if (!TryValidateModel(TokenInput, nameof(TokenInput)))
        {
            return Page();
        }

        var expiresInDays = ParseExpiresInDays(TokenInput.ExpiresInDays);
        if (expiresInDays == -1)
        {
            ModelState.AddModelError(
                $"{nameof(TokenInput)}.{nameof(CreateTokenInput.ExpiresInDays)}",
                "Invalid expiration.");
            return Page();
        }

        var result = _tokens.Create(userId, TokenInput.Name, expiresInDays == 0 ? null : expiresInDays);
        if (!result.Success || result.Value is null)
        {
            Error = result.Error ?? "Failed to create application token.";
            return RedirectToPage();
        }

        CreatedToken = result.Value.PlainTextToken;
        Success = $"Application token '{result.Value.Token.Name}' created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(CancellationToken cancellationToken)
    {
        if (!LoadAccount(out var userId))
        {
            return Forbid();
        }

        ModelState.Clear();
        if (!TryValidateModel(PasswordInput, nameof(PasswordInput)))
        {
            return Page();
        }

        if (!string.Equals(PasswordInput.NewPassword, PasswordInput.ConfirmNewPassword, StringComparison.Ordinal))
        {
            ModelState.Clear();
            ModelState.AddModelError(
                $"{nameof(PasswordInput)}.{nameof(ChangePasswordInput.ConfirmNewPassword)}",
                "Passwords do not match.");
            PasswordInput = new();
            return Page();
        }

        var result = await _users.ChangeOwnPasswordAsync(
            userId,
            PasswordInput.CurrentPassword,
            PasswordInput.NewPassword,
            cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to change password.";
            return RedirectToPage();
        }

        Success = "Password changed.";
        return RedirectToPage();
    }

    public IActionResult OnPostChangeTheme(string theme, string? returnUrl)
    {
        if (!LoadAccount(out var userId))
        {
            return Forbid();
        }

        var showMessage = string.IsNullOrWhiteSpace(returnUrl);
        if (!UserThemeAccessor.TryParse(theme, out var parsedTheme))
        {
            if (showMessage)
            {
                Error = "Invalid theme.";
            }

            return RedirectToLocal(returnUrl);
        }

        var result = _users.ChangeOwnTheme(userId, parsedTheme);
        if (!result.Success)
        {
            if (showMessage)
            {
                Error = result.Error ?? "Failed to change theme.";
            }

            return RedirectToLocal(returnUrl);
        }

        if (showMessage)
        {
            Success = $"Theme changed to {UserThemeAccessor.ToLabel(parsedTheme)}.";
        }

        return RedirectToLocal(returnUrl);
    }

    public IActionResult OnPostRevokeToken(Guid tokenId)
    {
        if (!LoadAccount(out var userId))
        {
            return Forbid();
        }

        var result = _tokens.Revoke(userId, tokenId);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to revoke application token.";
            return RedirectToPage();
        }

        Success = "Application token revoked.";
        return RedirectToPage();
    }

    public static string FormatTokenStatus(ApiToken token)
    {
        if (token.RevokedAt is not null)
        {
            return "Revoked";
        }

        if (token.ExpiresAt is not null && token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return "Expired";
        }

        return "Active";
    }

    private bool LoadAccount(out Guid userId)
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idStr, out userId))
        {
            return false;
        }

        var currentUserId = userId;
        var user = _users.ListUsers().FirstOrDefault(u => u.Id == currentUserId);
        if (user is null || !user.IsActive)
        {
            return false;
        }

        CurrentUser = user;
        RolesLabel = ToRolesLabel(user.Roles);
        ThemeLabel = UserThemeAccessor.ToLabel(user.Theme);
        Tokens = _tokens.ListForUser(userId);
        return true;
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToPage();
    }

    private static int ParseExpiresInDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "never", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return int.TryParse(value, out var days) && days is 30 or 90 or 365 ? days : -1;
    }

    private static string ToRolesLabel(PortalUserRoles roles)
    {
        if (roles == PortalUserRoles.None)
        {
            return "User";
        }

        var names = new List<string>();
        if (roles.HasFlag(PortalUserRoles.Owner))
        {
            names.Add(PortalUserRoleExtensions.OwnerClaim);
        }

        if (roles.HasFlag(PortalUserRoles.AdminUsers))
        {
            names.Add(PortalUserRoleExtensions.AdminUsersClaim);
        }

        if (roles.HasFlag(PortalUserRoles.RepoRead))
        {
            names.Add("repo.read");
        }

        if (roles.HasFlag(PortalUserRoles.RepoWrite))
        {
            names.Add("repo.write");
        }

        if (roles.HasFlag(PortalUserRoles.RepoAdmin))
        {
            names.Add("repo.admin");
        }

        if (roles.HasFlag(PortalUserRoles.RepoCreate))
        {
            names.Add("repo.create");
        }

        if (roles.HasFlag(PortalUserRoles.AdminSystem))
        {
            names.Add(PortalUserRoleExtensions.AdminSystemClaim);
        }

        if (roles.HasFlag(PortalUserRoles.RepoHooks))
        {
            names.Add(PortalUserRoleExtensions.RepoHooksClaim);
        }

        return names.Count == 0 ? "User" : string.Join(", ", names);
    }

    public sealed class CreateTokenInput
    {
        [Required]
        [StringLength(80)]
        [Display(Name = "Application name")]
        public string Name { get; set; } = "";

        [Display(Name = "Expiration")]
        public string ExpiresInDays { get; set; } = "90";
    }

    public sealed class ChangePasswordInput
    {
        [Required]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = "";

        [Required]
        [MinLength(8)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = "";

        [Required]
        [MinLength(8)]
        [Display(Name = "Confirm new password")]
        public string ConfirmNewPassword { get; set; } = "";
    }
}
