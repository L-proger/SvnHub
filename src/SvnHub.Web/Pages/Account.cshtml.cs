using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

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

    public IReadOnlyList<ApiToken> Tokens { get; private set; } = [];

    [BindProperty]
    public CreateTokenInput TokenInput { get; set; } = new();

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

        if (!ModelState.IsValid)
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
        Tokens = _tokens.ListForUser(userId);
        return true;
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

        if (roles.HasFlag(PortalUserRoles.AllAdmin))
        {
            return "Admin";
        }

        var names = new List<string>();
        if (roles.HasFlag(PortalUserRoles.AdminRepo))
        {
            names.Add("AdminRepo");
        }

        if (roles.HasFlag(PortalUserRoles.AdminSystem))
        {
            names.Add("AdminSystem");
        }

        if (roles.HasFlag(PortalUserRoles.AdminHooks))
        {
            names.Add("AdminHooks");
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
}
