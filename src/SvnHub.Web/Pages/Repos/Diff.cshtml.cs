using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class DiffModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;

    public DiffModel(RepositoryService repos, AccessService access, ISvnLookClient svnlook)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
    }

    public string RepoName { get; private set; } = "";
    public string Path { get; private set; } = "/";
    public long HeadRevision { get; private set; }
    public long Revision { get; private set; }
    public bool IsDirectory { get; private set; }

    public string? Author { get; private set; }
    public string? Age { get; private set; }
    public string? Message { get; private set; }
    public string? Diff { get; private set; }
    public bool IsTruncated { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string repoName,
        long rev,
        string? path,
        CancellationToken cancellationToken = default)
    {
        RepoName = repoName;
        Revision = rev;
        Path = Normalize(path);

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

        if (Revision <= 0)
        {
            return BadRequest("Revision is required.");
        }

        try
        {
            HeadRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            IsDirectory = await DetectIsDirectoryAsync(repo.LocalPath, Path, HeadRevision, cancellationToken);

            var dt = await _svnlook.GetRevisionDateAsync(repo.LocalPath, Revision, cancellationToken);
            var log = await _svnlook.GetRevisionLogAsync(repo.LocalPath, Revision, cancellationToken);
            var author = await _svnlook.GetRevisionAuthorAsync(repo.LocalPath, Revision, cancellationToken);

            Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim();
            Age = IndexModel.FormatUpdatedAgo(dt, DateTimeOffset.UtcNow);
            Message = FormatCommitMessage(log);

            var diff = await _svnlook.GetDiffAsync(repo.LocalPath, Path, Revision, cancellationToken);
            Diff = Cap(diff, 2 * 1024 * 1024);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return Page();
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
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

    private static string? FormatCommitMessage(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return null;
        }

        var firstLine = log
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        const int max = 160;
        return firstLine.Length <= max ? firstLine : firstLine[..max] + "…";
    }

    private string Cap(string diff, int maxChars)
    {
        if (diff.Length <= maxChars)
        {
            return diff;
        }

        IsTruncated = true;
        return diff[..maxChars];
    }

    private async Task<bool> DetectIsDirectoryAsync(string repoLocalPath, string path, long revision, CancellationToken cancellationToken)
    {
        if (path == "/")
        {
            return true;
        }

        try
        {
            _ = await _svnlook.GetFileSizeAsync(repoLocalPath, path, revision, cancellationToken);
            return false;
        }
        catch
        {
            try
            {
                _ = await _svnlook.ListTreeAsync(repoLocalPath, path, revision, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
