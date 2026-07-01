using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.App.Support;
using SvnHub.App.System;
using SvnHub.Domain;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly RepositoryManagementService _management;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;
    private readonly SettingsService _settings;

    public IndexModel(
        RepositoryService repos,
        RepositoryManagementService management,
        AccessService access,
        ISvnLookClient svnlook,
        SettingsService settings)
    {
        _repos = repos;
        _management = management;
        _access = access;
        _svnlook = svnlook;
        _settings = settings;
    }

    [TempData]
    public string? Message { get; set; }

    public IReadOnlyList<Repository> Repositories { get; private set; } = [];

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public IReadOnlyList<int> PageSizeOptions => PaginationOptions.PageSizes;

    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }
    public int FromIndex { get; private set; }
    public int ToIndex { get; private set; }

    public string? SearchQuery { get; private set; }
    public string? LabelFilter { get; private set; }
    public IReadOnlyList<string> LabelOptions { get; private set; } = [];

    public IReadOnlyDictionary<string, DateTimeOffset?> UpdatedAtByRepoName { get; private set; } =
        new Dictionary<string, DateTimeOffset?>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<Guid> BrowseableRepositoryIds { get; private set; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> ManageableRepositoryIds { get; private set; } = new HashSet<Guid>();

    public string SvnBaseUrl { get; private set; } = "";

    public string? GetCheckoutUrl(string repoName) => SvnCheckoutUrl.Build(SvnBaseUrl, repoName, "/");

    public async Task OnGetAsync(int p = 1, int? pageSize = null, string? q = null, string? label = null)
    {
        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            Repositories = [];
            return;
        }

        SvnBaseUrl = _settings.GetEffectiveSvnBaseUrl();
        PageSize = PaginationOptions.ResolvePageSize(Request, Response, pageSize);
        PageNumber = Math.Max(1, p);
        SearchQuery = NormalizeSearchQuery(q);
        LabelFilter = NormalizeSearchQuery(label);

        var browseable = new HashSet<Guid>();
        var manageable = new HashSet<Guid>();
        var accessible = _repos.List()
            .Where(r =>
            {
                var canBrowse = _access.GetAccess(userId.Value, r.Id, "/") >= AccessLevel.Read;
                var canManage = _management.CanMaintainRepository(userId.Value, r.Id);
                if (canBrowse)
                {
                    browseable.Add(r.Id);
                }

                if (canManage)
                {
                    manageable.Add(r.Id);
                }

                return canBrowse || canManage;
            })
            .ToArray();

        LabelOptions = RepositoryLabels.Collect(accessible);

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            accessible = accessible
                .Select(r => new
                {
                    Repository = r,
                    Score = FuzzySearchScorer.Score(r.Name, SearchQuery),
                })
                .Where(r => r.Score > 0)
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Repository.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => r.Repository)
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(LabelFilter))
        {
            accessible = accessible
                .Where(r => RepositoryLabels.Contains(r.Labels, LabelFilter))
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
        BrowseableRepositoryIds = browseable;
        ManageableRepositoryIds = manageable;

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

    public async Task<IActionResult> OnPostDiscoverAsync(int p = 1, int? pageSize = null, string? q = null, string? label = null, CancellationToken cancellationToken = default)
    {
        if (!(User?.IsInRole("AdminRepo") ?? false))
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
            return RedirectToPage(new { p, pageSize = PaginationOptions.ResolvePageSize(Request, Response, pageSize), q, label });
        }

        Message = result.Value == 0 ? "No new repositories found." : $"Discovered {result.Value} repository(ies).";
        return RedirectToPage(new { p, pageSize = PaginationOptions.ResolvePageSize(Request, Response, pageSize), q, label });
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
