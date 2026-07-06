using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "admin.users")]
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
    public IReadOnlyList<int> PageSizeOptions => PaginationOptions.PageSizes;
    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }
    public int FromIndex { get; private set; }
    public int ToIndex { get; private set; }
    public string? SearchQuery { get; private set; }

    public void OnGet(int p = 1, int? pageSize = null, string? q = null)
    {
        PageSize = PaginationOptions.ResolvePageSize(Request, Response, pageSize);
        PageNumber = Math.Max(1, p);
        SearchQuery = NormalizeSearchQuery(q);

        var all = _users.ListUsers();
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            all = all
                .Select(u => new
                {
                    User = u,
                    Score = FuzzySearchScorer.Score(u.UserName, SearchQuery),
                })
                .Where(u => u.Score > 0)
                .OrderByDescending(u => u.Score)
                .ThenBy(u => u.User.UserName, StringComparer.OrdinalIgnoreCase)
                .Select(u => u.User)
                .ToArray();
        }

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
        if (roles.HasFlag(PortalUserRoles.Owner)) parts.Add(PortalUserRoleExtensions.OwnerClaim);
        if (roles.HasFlag(PortalUserRoles.RepoAdmin)) parts.Add("repo.admin");
        if (roles.HasFlag(PortalUserRoles.RepoCreate)) parts.Add("repo.create");
        if (roles.HasFlag(PortalUserRoles.RepoRead)) parts.Add("repo.read");
        if (roles.HasFlag(PortalUserRoles.RepoWrite)) parts.Add("repo.write");
        if (roles.HasFlag(PortalUserRoles.AdminSystem)) parts.Add(PortalUserRoleExtensions.AdminSystemClaim);
        if (roles.HasFlag(PortalUserRoles.RepoHooks)) parts.Add(PortalUserRoleExtensions.RepoHooksClaim);
        if (roles.HasFlag(PortalUserRoles.AdminUsers)) parts.Add(PortalUserRoleExtensions.AdminUsersClaim);

        return string.Join(", ", parts);
    }

    private static string? NormalizeSearchQuery(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return null;
        }

        var trimmed = q.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
