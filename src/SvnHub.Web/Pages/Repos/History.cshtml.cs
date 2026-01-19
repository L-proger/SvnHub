using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class HistoryModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;

    public HistoryModel(RepositoryService repos, AccessService access, ISvnLookClient svnlook)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
    }

    public string RepoName { get; private set; } = "";
    public string Path { get; private set; } = "/";
    public string ParentPath { get; private set; } = "/";
    public long HeadRevision { get; private set; }
    public bool IsDirectory { get; private set; }
    public int Limit { get; private set; } = 50;
    public string? Error { get; private set; }

    public IReadOnlyList<HistoryRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string repoName, string? path, int limit = 50, CancellationToken cancellationToken = default)
    {
        RepoName = repoName;
        Path = Normalize(path);
        ParentPath = GetParent(Path);
        Limit = Math.Clamp(limit, 1, 500);

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
            HeadRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            IsDirectory = await DetectIsDirectoryAsync(repo.LocalPath, Path, HeadRevision, cancellationToken);
            var history = await _svnlook.GetHistoryAsync(repo.LocalPath, Path, HeadRevision, Limit, cancellationToken);

            if (history.Count == 0)
            {
                Rows = [];
                return Page();
            }

            const int concurrency = 4;
            using var gate = new SemaphoreSlim(concurrency, concurrency);
            var now = DateTimeOffset.UtcNow;

            async Task<HistoryRow> LoadOneAsync(SvnHistoryEntry entry)
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var dt = await _svnlook.GetRevisionDateAsync(repo.LocalPath, entry.Revision, cancellationToken);
                    var log = await _svnlook.GetRevisionLogAsync(repo.LocalPath, entry.Revision, cancellationToken);
                    var author = await _svnlook.GetRevisionAuthorAsync(repo.LocalPath, entry.Revision, cancellationToken);
                    return new HistoryRow(
                        Revision: entry.Revision,
                        Author: string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
                        Age: IndexModel.FormatUpdatedAgo(dt, now),
                        Message: FormatCommitMessage(log),
                        ChangedPath: entry.Path
                    );
                }
                catch
                {
                    return new HistoryRow(
                        Revision: entry.Revision,
                        Author: null,
                        Age: null,
                        Message: null,
                        ChangedPath: entry.Path
                    );
                }
                finally
                {
                    gate.Release();
                }
            }

            Rows = await Task.WhenAll(history.Select(LoadOneAsync));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return Page();
    }

    public sealed record HistoryRow(long Revision, string? Author, string? Age, string? Message, string? ChangedPath);

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

        const int max = 80;
        return firstLine.Length <= max ? firstLine : firstLine[..max] + "…";
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
