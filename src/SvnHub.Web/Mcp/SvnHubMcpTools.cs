using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SvnHub.App.Configuration;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;
using SvnHub.Web.Support;

namespace SvnHub.Web.Mcp;

[Authorize]
[McpServerToolType]
public sealed class SvnHubMcpTools
{
    private const int DefaultMaxFileChars = 200_000;
    private const int MaxFileChars = 1_000_000;
    private const int DefaultMaxDiffChars = 200_000;
    private const int MaxDiffChars = 2 * 1024 * 1024;

    [McpServerTool(
        Name = "svnhub_list_repositories",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("List SVN repositories visible to the authenticated SvnHub user.")]
    public static IReadOnlyList<McpRepositoryInfo> ListRepositories(
        RepositoryService repositories,
        AccessService access,
        IHttpContextAccessor httpContextAccessor)
    {
        var userId = GetCurrentUserId(httpContextAccessor);

        return repositories.List()
            .Where(r => access.GetAccess(userId, r.Id, "/") >= AccessLevel.Read)
            .Select(r => new McpRepositoryInfo(
                r.Name,
                r.CreatedAt,
                access.GetAccess(userId, r.Id, "/").ToString(),
                r.AuthenticatedDefaultAccess?.ToString()))
            .ToArray();
    }

    [McpServerTool(
        Name = "svnhub_list_tree",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("List one repository directory at a revision, filtered by SvnHub read permissions.")]
    public static async Task<McpTreeResult> ListTreeAsync(
        [Description("Repository name.")] string repoName,
        [Description("Repository path. Use / for root.")] string? path,
        [Description("Optional revision. Defaults to HEAD.")] long? revision,
        RepositoryService repositories,
        AccessService access,
        ISvnLookClient svnlook,
        IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId(httpContextAccessor);
        var repo = GetRepository(repositories, repoName);
        var normalizedPath = NormalizePath(path);
        RequireRead(access, userId, repo, normalizedPath);

        var head = await svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
        var effectiveRevision = ResolveRevision(revision, head);
        var entries = await svnlook.ListTreeAsync(repo.LocalPath, normalizedPath, effectiveRevision, cancellationToken);

        var rows = entries
            .Where(e => access.GetAccess(userId, repo.Id, e.Path) >= AccessLevel.Read)
            .Select(e => new McpTreeEntry(e.Name, e.Path, e.IsDirectory))
            .ToArray();

        return new McpTreeResult(repo.Name, normalizedPath, effectiveRevision, rows);
    }

    [McpServerTool(
        Name = "svnhub_read_file",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Read a text file from a repository. Binary and oversized files return metadata without contents.")]
    public static async Task<McpFileReadResult> ReadFileAsync(
        [Description("Repository name.")] string repoName,
        [Description("File path inside the repository.")] string path,
        [Description("Optional revision. Defaults to HEAD.")] long? revision,
        [Description("Maximum characters to return. Defaults to 200000 and caps at 1000000.")] int? maxChars,
        RepositoryService repositories,
        AccessService access,
        ISvnLookClient svnlook,
        SvnHubOptions options,
        IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId(httpContextAccessor);
        var repo = GetRepository(repositories, repoName);
        var normalizedPath = NormalizePath(path);
        if (normalizedPath == "/")
        {
            throw new InvalidOperationException("File path is required.");
        }

        RequireRead(access, userId, repo, normalizedPath);

        var head = await svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
        var effectiveRevision = ResolveRevision(revision, head);
        var fileSize = await svnlook.GetFileSizeAsync(repo.LocalPath, normalizedPath, effectiveRevision, cancellationToken);
        var maxPreviewBytes = options.GetEffectiveMaxPreviewBytes();
        var language = RepositoryFileClassifier.GuessLanguage(normalizedPath);

        if (fileSize > maxPreviewBytes)
        {
            return McpFileReadResult.NoPreview(
                repo.Name,
                normalizedPath,
                effectiveRevision,
                fileSize,
                language,
                $"File is larger than MaxPreviewBytes ({fileSize} > {maxPreviewBytes}).");
        }

        var sniffBytes = await svnlook.CatPrefixBytesAsync(
            repo.LocalPath,
            normalizedPath,
            effectiveRevision,
            RepositoryFileClassifier.SniffByteCount,
            cancellationToken);

        if (RepositoryFileClassifier.LooksBinary(sniffBytes))
        {
            return McpFileReadResult.NoPreview(
                repo.Name,
                normalizedPath,
                effectiveRevision,
                fileSize,
                language,
                "File appears to be binary.");
        }

        var bytes = fileSize <= sniffBytes.Length
            ? sniffBytes
            : await svnlook.CatBytesAsync(repo.LocalPath, normalizedPath, effectiveRevision, cancellationToken);

        var content = RepositoryFileClassifier.DecodeText(bytes);
        var charLimit = Math.Clamp(maxChars ?? DefaultMaxFileChars, 1, MaxFileChars);
        var truncated = content.Length > charLimit;
        if (truncated)
        {
            content = content[..charLimit];
        }

        return new McpFileReadResult(
            repo.Name,
            normalizedPath,
            effectiveRevision,
            fileSize,
            language,
            PreviewAvailable: true,
            Reason: null,
            Content: content,
            Truncated: truncated);
    }

    [McpServerTool(
        Name = "svnhub_get_history",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Get recent SVN history for a repository path.")]
    public static async Task<IReadOnlyList<McpHistoryEntry>> GetHistoryAsync(
        [Description("Repository name.")] string repoName,
        [Description("Repository path. Use / for root.")] string? path,
        [Description("Maximum history entries. Defaults to 50 and caps at 100.")] int? limit,
        RepositoryService repositories,
        AccessService access,
        ISvnLookClient svnlook,
        IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId(httpContextAccessor);
        var repo = GetRepository(repositories, repoName);
        var normalizedPath = NormalizePath(path);
        RequireRead(access, userId, repo, normalizedPath);

        var head = await svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var history = await svnlook.GetHistoryAsync(repo.LocalPath, normalizedPath, head, take, cancellationToken);

        var rows = new List<McpHistoryEntry>(history.Count);
        foreach (var entry in history)
        {
            DateTimeOffset? date = null;
            string? author = null;
            string? message = null;

            try
            {
                date = await svnlook.GetRevisionDateAsync(repo.LocalPath, entry.Revision, cancellationToken);
                author = NullIfWhiteSpace(await svnlook.GetRevisionAuthorAsync(repo.LocalPath, entry.Revision, cancellationToken));
                message = FirstLineOrNull(await svnlook.GetRevisionLogAsync(repo.LocalPath, entry.Revision, cancellationToken));
            }
            catch
            {
                // Keep the history row even when extra revision metadata is unavailable.
            }

            rows.Add(new McpHistoryEntry(entry.Revision, entry.Path, date, author, message));
        }

        return rows;
    }

    [McpServerTool(
        Name = "svnhub_get_diff",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Get an SVN diff for a revision and optional repository path.")]
    public static async Task<McpDiffResult> GetDiffAsync(
        [Description("Repository name.")] string repoName,
        [Description("Revision to diff.")] long revision,
        [Description("Repository path. Use / for the whole revision.")] string? path,
        [Description("Maximum characters to return. Defaults to 200000 and caps at 2097152.")] int? maxChars,
        RepositoryService repositories,
        AccessService access,
        ISvnLookClient svnlook,
        IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        if (revision <= 0)
        {
            throw new InvalidOperationException("Revision must be positive.");
        }

        var userId = GetCurrentUserId(httpContextAccessor);
        var repo = GetRepository(repositories, repoName);
        var normalizedPath = NormalizePath(path);
        RequireRead(access, userId, repo, normalizedPath);

        var head = await svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
        if (revision > head)
        {
            throw new InvalidOperationException($"Revision r{revision} is newer than HEAD r{head}.");
        }

        var diff = await svnlook.GetDiffAsync(repo.LocalPath, normalizedPath, revision, cancellationToken);
        var charLimit = Math.Clamp(maxChars ?? DefaultMaxDiffChars, 1, MaxDiffChars);
        var truncated = diff.Length > charLimit;
        if (truncated)
        {
            diff = diff[..charLimit];
        }

        return new McpDiffResult(repo.Name, normalizedPath, revision, diff, truncated);
    }

    private static Guid GetCurrentUserId(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var userId = user is null ? null : AccessService.GetUserIdFromClaimsPrincipal(user);
        if (userId is null)
        {
            throw new UnauthorizedAccessException("Authenticated SvnHub user is required.");
        }

        return userId.Value;
    }

    private static Repository GetRepository(RepositoryService repositories, string repoName)
    {
        var repo = repositories.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            throw new InvalidOperationException("Repository not found.");
        }

        return repo;
    }

    private static void RequireRead(AccessService access, Guid userId, Repository repository, string path)
    {
        if (access.GetAccess(userId, repository.Id, path) < AccessLevel.Read)
        {
            throw new UnauthorizedAccessException("Read access denied.");
        }
    }

    private static long ResolveRevision(long? requested, long head)
    {
        if (requested is null)
        {
            return head;
        }

        if (requested.Value <= 0 || requested.Value > head)
        {
            throw new InvalidOperationException($"Invalid revision: r{requested.Value}.");
        }

        return requested.Value;
    }

    private static string NormalizePath(string? path)
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

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstLineOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }
}

public sealed record McpRepositoryInfo(
    string Name,
    DateTimeOffset CreatedAt,
    string RootAccess,
    string? AuthenticatedDefaultAccess);

public sealed record McpTreeResult(
    string Repository,
    string Path,
    long Revision,
    IReadOnlyList<McpTreeEntry> Entries);

public sealed record McpTreeEntry(string Name, string Path, bool IsDirectory);

public sealed record McpFileReadResult(
    string Repository,
    string Path,
    long Revision,
    long SizeBytes,
    string Language,
    bool PreviewAvailable,
    string? Reason,
    string? Content,
    bool Truncated)
{
    public static McpFileReadResult NoPreview(
        string repository,
        string path,
        long revision,
        long sizeBytes,
        string language,
        string reason) =>
        new(repository, path, revision, sizeBytes, language, false, reason, null, false);
}

public sealed record McpHistoryEntry(
    long Revision,
    string Path,
    DateTimeOffset? Date,
    string? Author,
    string? Message);

public sealed record McpDiffResult(
    string Repository,
    string Path,
    long Revision,
    string Diff,
    bool Truncated);
