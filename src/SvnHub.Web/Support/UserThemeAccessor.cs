using System.Security.Claims;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Support;

public sealed class UserThemeAccessor
{
    private readonly UserService _users;

    public UserThemeAccessor(UserService users)
    {
        _users = users;
    }

    public PortalUserTheme GetTheme(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return PortalUserTheme.System;
        }

        var idStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idStr, out var userId))
        {
            return PortalUserTheme.System;
        }

        var user = _users.ListUsers().FirstOrDefault(u => u.Id == userId && u.IsActive);
        return IsKnownTheme(user?.Theme) ? user!.Theme : PortalUserTheme.System;
    }

    public string GetBootstrapTheme(ClaimsPrincipal? principal) => ToBootstrapTheme(GetTheme(principal));

    public static string ToBootstrapTheme(PortalUserTheme theme) =>
        theme == PortalUserTheme.Light ? "light" : "dark";

    public static bool TryParse(string? value, out PortalUserTheme theme)
    {
        if (Enum.TryParse(value, ignoreCase: true, out theme) && IsKnownTheme(theme))
        {
            return true;
        }

        theme = PortalUserTheme.System;
        return false;
    }

    public static string ToLabel(PortalUserTheme theme) =>
        theme switch
        {
            PortalUserTheme.Light => "Light",
            PortalUserTheme.Dark => "Dark",
            _ => "System",
        };

    private static bool IsKnownTheme(PortalUserTheme? theme) =>
        theme is PortalUserTheme.System or PortalUserTheme.Dark or PortalUserTheme.Light;
}
