using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "AdminSystem")]
public sealed class UsersModel : PageModel
{
    private readonly UserService _users;

    public UsersModel(UserService users)
    {
        _users = users;
    }

    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? Success { get; set; }

    public IReadOnlyList<PortalUser> Users { get; private set; } = [];

    public int PageNumber { get; private set; } = 1;
    public int PageSize { get; private set; } = 10;
    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }
    public int FromIndex { get; private set; }
    public int ToIndex { get; private set; }

    public void OnGet(int p = 1, int pageSize = 10)
    {
        PageSize = NormalizePageSize(pageSize);
        PageNumber = Math.Max(1, p);

        var all = _users.ListUsers();
        TotalCount = all.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        var skip = (PageNumber - 1) * PageSize;
        Users = all.Skip(skip).Take(PageSize).ToArray();

        if (TotalCount == 0)
        {
            FromIndex = 0;
            ToIndex = 0;
        }
        else
        {
            FromIndex = skip + 1;
            ToIndex = skip + Users.Count;
        }
    }

    public static string ToRolesLabel(PortalUserRoles roles)
    {
        if (roles == PortalUserRoles.None)
        {
            return "User";
        }

        var parts = new List<string>();
        if (roles.HasFlag(PortalUserRoles.AdminRepo)) parts.Add(nameof(PortalUserRoles.AdminRepo));
        if (roles.HasFlag(PortalUserRoles.AdminSystem)) parts.Add(nameof(PortalUserRoles.AdminSystem));
        if (roles.HasFlag(PortalUserRoles.AdminHooks)) parts.Add(nameof(PortalUserRoles.AdminHooks));

        return string.Join(", ", parts);
    }

    private static int NormalizePageSize(int pageSize) =>
        pageSize switch
        {
            10 => 10,
            25 => 25,
            50 => 50,
            100 => 100,
            _ => 10,
        };
}
