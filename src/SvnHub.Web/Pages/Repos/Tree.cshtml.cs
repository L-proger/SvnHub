using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class TreeModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;
    private readonly ISvnRepositoryWriter _svnWriter;
    private readonly SettingsService _settings;

    public TreeModel(
        RepositoryService repos,
        AccessService access,
        ISvnLookClient svnlook,
        ISvnRepositoryWriter svnWriter,
        SettingsService settings)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
        _svnWriter = svnWriter;
        _settings = settings;
    }

    public string RepoName { get; private set; } = "";
    public string Path { get; private set; } = "/";
    public string ParentPath { get; private set; } = "/";
    public long Revision { get; private set; }
    public IReadOnlyList<SvnTreeEntry> Entries { get; private set; } = [];
    public ISet<string> DeletablePaths { get; private set; } = new HashSet<string>(StringComparer.Ordinal);
    public string? Error { get; private set; }
    public bool HasReadme { get; private set; }
    public string ReadmeHtml { get; private set; } = "";
    public string SvnBaseUrl { get; private set; } = "";

    public string? GetCheckoutUrl(string entryPath) => SvnCheckoutUrl.Build(SvnBaseUrl, RepoName, entryPath);

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? FlashError { get; set; }

    public async Task<IActionResult> OnGetAsync(string repoName, string? path, CancellationToken cancellationToken)
    {
        RepoName = repoName;
        Path = Normalize(path);
        ParentPath = GetParent(Path);
        SvnBaseUrl = _settings.GetEffectiveSvnBaseUrl();

        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            return Forbid();
        }

        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (_access.GetAccess(userId.Value, repo.Id, Path) < AccessLevel.Read)
        {
            return Forbid();
        }

        try
        {
            Revision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            Entries = await _svnlook.ListTreeAsync(repo.LocalPath, Path, Revision, cancellationToken);

            DeletablePaths = Entries
                .Where(e => _access.GetAccess(userId.Value, repo.Id, e.Path) >= AccessLevel.Write)
                .Select(e => e.Path)
                .ToHashSet(StringComparer.Ordinal);

            var readme = Entries.FirstOrDefault(e =>
                !e.IsDirectory &&
                string.Equals(e.Name, "README.md", StringComparison.OrdinalIgnoreCase));

            if (readme is not null)
            {
                var readmeText = await _svnlook.CatAsync(repo.LocalPath, readme.Path, Revision, cancellationToken);
                if (readmeText.Length > 200_000)
                {
                    readmeText = readmeText[..200_000];
                }
                ReadmeHtml = MarkdownRenderer.Render(readmeText, repoName, readme.Path);
                HasReadme = true;
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteEntryAsync(
        string repoName,
        string? path,
        string targetPath,
        CancellationToken cancellationToken)
    {
        RepoName = repoName;
        Path = Normalize(path);
        ParentPath = GetParent(Path);

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            FlashError = "Invalid path.";
            return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
        }

        var normalizedTarget = Normalize(targetPath);
        if (normalizedTarget == "/")
        {
            FlashError = "Refusing to delete repository root.";
            return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
        }

        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            return Forbid();
        }

        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (_access.GetAccess(userId.Value, repo.Id, normalizedTarget) < AccessLevel.Write)
        {
            return Forbid();
        }

        var actor = User?.Identity?.Name ?? userId.Value.ToString("D");
        var message = $"Delete {normalizedTarget} via SvnHub (by {actor})";

        try
        {
            await _svnWriter.DeleteAsync(repo.LocalPath, normalizedTarget, message, cancellationToken);
        }
        catch (Exception ex)
        {
            FlashError = ex.Message;
            return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
        }

        Message = $"Deleted {System.IO.Path.GetFileName(normalizedTarget)}";
        return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var p = path.Trim();
        if (!p.StartsWith('/'))
        {
            p = "/" + p;
        }

        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        if (p.Length > 1 && p.EndsWith('/'))
        {
            p = p.TrimEnd('/');
        }

        if (p.Contains("/../", StringComparison.Ordinal) || p.EndsWith("/..", StringComparison.Ordinal) || p == "/..")
        {
            return "/";
        }

        return p;
    }

    private static string GetParent(string path)
    {
        if (path == "/")
        {
            return "/";
        }

        var idx = path.LastIndexOf('/');
        if (idx <= 0)
        {
            return "/";
        }

        return path[..idx];
    }
}
