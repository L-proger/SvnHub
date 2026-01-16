using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;

    public IndexModel(RepositoryService repos, AccessService access, ISvnLookClient svnlook)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
    }

    [TempData]
    public string? Message { get; set; }

    public IReadOnlyList<Repository> Repositories { get; private set; } = [];

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }
    public int FromIndex { get; private set; }
    public int ToIndex { get; private set; }

    public string? SearchQuery { get; private set; }

    public IReadOnlyDictionary<string, DateTimeOffset?> UpdatedAtByRepoName { get; private set; } =
        new Dictionary<string, DateTimeOffset?>(StringComparer.OrdinalIgnoreCase);

    public async Task OnGetAsync(int p = 1, int pageSize = 10, string? q = null)
    {
        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            Repositories = [];
            return;
        }

        PageSize = NormalizePageSize(pageSize);
        PageNumber = Math.Max(1, p);
        SearchQuery = NormalizeSearchQuery(q);

        var accessible = _repos.List()
            .Where(r => _access.GetAccess(userId.Value, r.Id, "/") >= AccessLevel.Read)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            accessible = accessible
                .Where(r => r.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        TotalCount = accessible.Length;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        var skip = (PageNumber - 1) * PageSize;
        Repositories = accessible.Skip(skip).Take(PageSize).ToArray();

        if (TotalCount == 0)
        {
            FromIndex = 0;
            ToIndex = 0;
        }
        else
        {
            FromIndex = skip + 1;
            ToIndex = skip + Repositories.Count;
        }

        UpdatedAtByRepoName = await LoadUpdatedDatesAsync(Repositories, HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostDiscoverAsync(int p = 1, int pageSize = 10, string? q = null, CancellationToken cancellationToken = default)
    {
        if (!(User?.IsInRole("Admin") ?? false))
        {
            return Forbid();
        }

        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            return Forbid();
        }

        var result = await _repos.DiscoverAsync(userId.Value, cancellationToken);
        if (!result.Success)
        {
            Message = result.Error ?? "Discover failed.";
            return RedirectToPage(new { p, pageSize, q });
        }

        Message = result.Value == 0 ? "No new repositories found." : $"Discovered {result.Value} repository(ies).";
        return RedirectToPage(new { p, pageSize, q });
    }

    public static string FormatUpdatedAgo(DateTimeOffset updatedAt, DateTimeOffset now)
    {
        var delta = now - updatedAt;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            var minutes = (int)Math.Round(delta.TotalMinutes);
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            var hours = (int)Math.Round(delta.TotalHours);
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        var days = (int)Math.Floor(delta.TotalDays);
        if (days < 30)
        {
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }

        var months = (int)Math.Floor(days / 30d);
        if (months < 12)
        {
            return months == 1 ? "1 month ago" : $"{months} months ago";
        }

        var years = (int)Math.Floor(days / 365d);
        return years == 1 ? "1 year ago" : $"{years} years ago";
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

    private static string? NormalizeSearchQuery(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return null;
        }

        var trimmed = q.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private async Task<IReadOnlyDictionary<string, DateTimeOffset?>> LoadUpdatedDatesAsync(
        IReadOnlyList<Repository> repositories,
        CancellationToken cancellationToken)
    {
        if (repositories.Count == 0)
        {
            return new Dictionary<string, DateTimeOffset?>(StringComparer.OrdinalIgnoreCase);
        }

        async Task<(string Name, DateTimeOffset? UpdatedAt)> LoadOneAsync(Repository r)
        {
            try
            {
                var dt = await _svnlook.GetHeadChangedAtAsync(r.LocalPath, cancellationToken);
                return (r.Name, dt);
            }
            catch
            {
                return (r.Name, null);
            }
        }

        var results = await Task.WhenAll(repositories.Select(LoadOneAsync));
        return results.ToDictionary(x => x.Name, x => x.UpdatedAt, StringComparer.OrdinalIgnoreCase);
    }
}
