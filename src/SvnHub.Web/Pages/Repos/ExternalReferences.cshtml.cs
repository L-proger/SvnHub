using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class ExternalReferencesModel : PageModel
{
    private readonly RepositoryService _repositories;
    private readonly RepositoryExternalReferenceService _references;
    private readonly AccessService _access;

    public ExternalReferencesModel(
        RepositoryService repositories,
        RepositoryExternalReferenceService references,
        AccessService access)
    {
        _repositories = repositories;
        _references = references;
        _access = access;
    }

    public string RepoName { get; private set; } = "";
    public string Query { get; private set; } = "";
    public string TargetBranch { get; private set; } = "";
    public string SourceBranch { get; private set; } = "";
    public string Pinning { get; private set; } = "";
    public int PageNumber { get; private set; } = 1;
    public int PageSize { get; private set; } = PaginationOptions.PageSizes[0];
    public int TotalRows { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)PageSize));
    public int ShowingStart => TotalRows == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
    public int ShowingEnd => Math.Min(PageNumber * PageSize, TotalRows);
    public int TotalReferenceCount { get; private set; }
    public int SourceRepositoryCount { get; private set; }
    public int IncompleteRepositoryCount { get; private set; }
    public DateTimeOffset? LastIndexSuccessAt { get; private set; }
    public IReadOnlyList<int> PageSizeOptions => PaginationOptions.PageSizes;
    public IReadOnlyList<string> TargetBranchOptions { get; private set; } = [];
    public IReadOnlyList<string> SourceBranchOptions { get; private set; } = [];
    public IReadOnlyList<RepositoryExternalReference> Rows { get; private set; } = [];
    public bool HasFilters =>
        Query.Length > 0 ||
        TargetBranch.Length > 0 ||
        SourceBranch.Length > 0 ||
        Pinning.Length > 0;

    public async Task<IActionResult> OnGetAsync(
        string repoName,
        string? q = null,
        string? targetBranch = null,
        string? sourceBranch = null,
        string? pinning = null,
        int p = 1,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            return Forbid();
        }

        var repository = _repositories.FindByName(repoName);
        if (repository is null)
        {
            return NotFound();
        }

        if (_access.GetAccess(userId.Value, repository.Id, "/") < AccessLevel.Read)
        {
            return Forbid();
        }

        RepoName = repository.Name;
        Query = (q ?? "").Trim();
        TargetBranch = (targetBranch ?? "").Trim();
        SourceBranch = (sourceBranch ?? "").Trim();
        Pinning = NormalizePinning(pinning);
        PageSize = PaginationOptions.ResolvePageSize(Request, Response, pageSize);

        var snapshot = await _references.ListIncomingAsync(userId.Value, repository.Id, cancellationToken);
        var allRows = snapshot.References;
        TotalReferenceCount = allRows.Count;
        SourceRepositoryCount = allRows
            .Select(row => row.SourceRepositoryId)
            .Distinct()
            .Count();
        IncompleteRepositoryCount = snapshot.IncompleteRepositoryCount;
        LastIndexSuccessAt = snapshot.LastIndexSuccessAt;
        TargetBranchOptions = BuildBranchOptions(allRows.Select(row => row.TargetBranch), TargetBranch);
        SourceBranchOptions = BuildBranchOptions(allRows.Select(row => row.SourceBranch), SourceBranch);

        IEnumerable<RepositoryExternalReference> filtered = allRows;
        if (TargetBranch.Length > 0)
        {
            filtered = filtered.Where(row =>
                string.Equals(row.TargetBranch, TargetBranch, StringComparison.OrdinalIgnoreCase));
        }

        if (SourceBranch.Length > 0)
        {
            filtered = filtered.Where(row =>
                string.Equals(row.SourceBranch, SourceBranch, StringComparison.OrdinalIgnoreCase));
        }

        if (Pinning == "pinned")
        {
            filtered = filtered.Where(row => row.IsPinned);
        }
        else if (Pinning == "unpinned")
        {
            filtered = filtered.Where(row => !row.IsPinned);
        }

        if (Query.Length > 0)
        {
            filtered = filtered.Where(row => MatchesQuery(row, Query));
        }

        var filteredRows = filtered.ToArray();
        TotalRows = filteredRows.Length;
        PageNumber = Math.Clamp(p, 1, TotalPages);
        Rows = filteredRows
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToArray();
        return Page();
    }

    private static IReadOnlyList<string> BuildBranchOptions(
        IEnumerable<string> branches,
        string selectedBranch)
    {
        var values = branches
            .Append(selectedBranch)
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(branch => BranchSortOrder(branch))
            .ThenBy(branch => branch, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values;
    }

    private static int BranchSortOrder(string branch) => branch switch
    {
        "trunk" => 0,
        _ when branch.StartsWith("branches/", StringComparison.OrdinalIgnoreCase) => 1,
        _ when branch.StartsWith("tags/", StringComparison.OrdinalIgnoreCase) => 2,
        _ => 3,
    };

    private static bool MatchesQuery(RepositoryExternalReference row, string query) =>
        row.SourceRepositoryName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        row.SourceParentPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        row.MountedPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        row.TargetPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        row.RawDefinition.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePinning(string? pinning) =>
        pinning?.Trim().ToLowerInvariant() is "pinned" or "unpinned"
            ? pinning.Trim().ToLowerInvariant()
            : "";
}
