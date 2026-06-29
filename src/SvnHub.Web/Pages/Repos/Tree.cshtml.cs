using System.IO.Compression;
using System.Buffers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.App.Support;
using SvnHub.App.System;
using SvnHub.App.Configuration;
using SvnHub.Domain;
using SvnHub.Web.Support;
using Microsoft.AspNetCore.Http;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class TreeModel : PageModel
{
    private static readonly string[] ReadmeFileNames =
    [
        "README",
        "README.md",
        "README.mkd",
        "README.markdown",
        "README.txt",
        "README.rst",
        "README.adoc",
        "README.asciidoc",
    ];

    private static readonly TimeSpan ExternalZipExportTimeout = TimeSpan.FromMinutes(2);

    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;
    private readonly ISvnRepositoryWriter _svnWriter;
    private readonly SettingsService _settings;
    private readonly ICommandRunner _runner;
    private readonly SvnHubOptions _options;

    public TreeModel(
        RepositoryService repos,
        AccessService access,
        ISvnLookClient svnlook,
        ISvnRepositoryWriter svnWriter,
        SettingsService settings,
        ICommandRunner runner,
        SvnHubOptions options)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
        _svnWriter = svnWriter;
        _settings = settings;
        _runner = runner;
        _options = options;
    }

    public string RepoName { get; private set; } = "";
    public string Path { get; private set; } = "/";
    public string ParentPath { get; private set; } = "/";
    public long HeadRevision { get; private set; }
    public long Revision { get; private set; }
    public long? ViewRevision { get; private set; }
    public IReadOnlyList<string> Labels { get; private set; } = [];
    public IReadOnlyList<SvnTreeEntry> Entries { get; private set; } = [];
    public IReadOnlyList<TreeRow> Rows { get; private set; } = [];
    public ISet<string> DeletablePaths { get; private set; } = new HashSet<string>(StringComparer.Ordinal);
    public ISet<string> ZipPaths { get; private set; } = new HashSet<string>(StringComparer.Ordinal);
    public bool CanWriteHere { get; private set; }
    public bool CanWriteActions { get; private set; }
    public int DirectoryCount { get; private set; }
    public int FileCount { get; private set; }
    public int ExternalCount { get; private set; }
    public string? Error { get; private set; }
    public bool HasReadme { get; private set; }
    public string ReadmeHtml { get; private set; } = "";
    public string ReadmePath { get; private set; } = "";
    public bool CanEditReadme { get; private set; }
    public string SvnBaseUrl { get; private set; } = "";

    public string? GetCheckoutUrl(string entryPath) => SvnCheckoutUrl.Build(SvnBaseUrl, RepoName, entryPath);

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? FlashError { get; set; }

    public async Task<IActionResult> OnGetAsync(
        string repoName,
        string? path,
        long? rev,
        bool defaultPath = false,
        CancellationToken cancellationToken = default)
    {
        RepoName = repoName;
        Path = Normalize(path);
        ParentPath = GetParent(Path);
        SvnBaseUrl = _settings.GetEffectiveSvnBaseUrl();
        ViewRevision = rev;

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

        Labels = RepositoryLabels.Normalize(repo.Labels);

        if (_access.GetAccess(userId.Value, repo.Id, Path) < AccessLevel.Read)
        {
            return Forbid();
        }

        CanWriteHere = _access.GetAccess(userId.Value, repo.Id, Path) >= AccessLevel.Write;
        CanWriteActions = CanWriteHere && rev is null;

        IReadOnlyList<SvnTreeEntry>? preloadedEntries = null;

        try
        {
            HeadRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            Revision = ResolveRevision(rev, HeadRevision);
            if (defaultPath && Path == "/")
            {
                preloadedEntries = await _svnlook.ListTreeAsync(repo.LocalPath, Path, Revision, cancellationToken);
                var trunkPath = GetDefaultTrunkPath(preloadedEntries);
                if (trunkPath is not null && _access.GetAccess(userId.Value, repo.Id, trunkPath) >= AccessLevel.Read)
                {
                    return rev is null
                        ? RedirectToPage(new { repoName, path = RoutePath(trunkPath) })
                        : RedirectToPage(new { repoName, path = RoutePath(trunkPath), rev });
                }
            }

            Entries = preloadedEntries ?? await _svnlook.ListTreeAsync(repo.LocalPath, Path, Revision, cancellationToken);
            DirectoryCount = Entries.Count(e => e.IsDirectory);
            FileCount = Entries.Count(e => !e.IsDirectory);
            var entryRows = await LoadRowsAsync(repo.LocalPath, Entries, Revision, cancellationToken);
            var externalRows = await LoadExternalRowsAsync(repo, Revision, cancellationToken);
            ExternalCount = externalRows.Count;
            Rows = MergeRows(entryRows, externalRows);

            DeletablePaths = CanWriteActions
                ? Entries
                    .Where(e => _access.GetAccess(userId.Value, repo.Id, e.Path) >= AccessLevel.Write)
                    .Select(e => e.Path)
                    .ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            ZipPaths = Entries
                .Where(e => e.IsDirectory && _access.GetAccess(userId.Value, repo.Id, e.Path) >= AccessLevel.Read)
                .Select(e => e.Path)
                .ToHashSet(StringComparer.Ordinal);

            var readme = Entries
                .Where(e => !e.IsDirectory && IsReadmeFileName(e.Name))
                .OrderBy(e => GetReadmePriority(e.Name))
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (readme is not null)
            {
                var maxPreviewBytes = _options.GetEffectiveMaxPreviewBytes();
                var readmeSize = await _svnlook.GetFileSizeAsync(repo.LocalPath, readme.Path, Revision, cancellationToken);
                if (readmeSize <= maxPreviewBytes)
                {
                    var readmeSniffBytes = await _svnlook.CatPrefixBytesAsync(
                        repo.LocalPath,
                        readme.Path,
                        Revision,
                        RepositoryFileClassifier.SniffByteCount,
                        cancellationToken);

                    if (!RepositoryFileClassifier.LooksBinary(readmeSniffBytes))
                    {
                        var readmeBytes = readmeSize <= readmeSniffBytes.Length
                            ? readmeSniffBytes
                            : await _svnlook.CatBytesAsync(repo.LocalPath, readme.Path, Revision, cancellationToken);

                        var readmeText = RepositoryFileClassifier.DecodeText(readmeBytes);
                        if (readmeText.Length > 200_000)
                        {
                            readmeText = readmeText[..200_000];
                        }

                        ReadmeHtml = MarkdownRenderer.Render(readmeText, repoName, readme.Path, rev);
                        ReadmePath = readme.Path;
                        HasReadme = true;
                        CanEditReadme = CanWriteActions && _access.GetAccess(userId.Value, repo.Id, readme.Path) >= AccessLevel.Write;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            DirectoryCount = 0;
            FileCount = 0;
            ExternalCount = 0;
        }

        return Page();
    }

    private static bool IsReadmeFileName(string name) =>
        ReadmeFileNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    private static int GetReadmePriority(string name)
    {
        // Prefer Markdown first (common default), then plain README, then others.
        if (string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "README.markdown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "README.mkd", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(name, "README", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static string? GetDefaultTrunkPath(IReadOnlyList<SvnTreeEntry> entries)
    {
        if (entries.Count == 0 || entries.Any(e => !e.IsDirectory))
        {
            return null;
        }

        var allowedRootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "trunk",
            "branches",
            "tags",
        };

        if (entries.Any(e => !allowedRootNames.Contains(e.Name)))
        {
            return null;
        }

        var trunk = entries.FirstOrDefault(e => string.Equals(e.Name, "trunk", StringComparison.OrdinalIgnoreCase));
        if (trunk is null)
        {
            return null;
        }

        var normalizedPath = Normalize(trunk.Path);
        return normalizedPath == "/" ? "/trunk" : normalizedPath;
    }

    private async Task<IReadOnlyList<TreeRow>> LoadRowsAsync(
        string repoLocalPath,
        IReadOnlyList<SvnTreeEntry> entries,
        long headRevision,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return Array.Empty<TreeRow>();
        }

        const int concurrency = 4;
        using var gate = new SemaphoreSlim(concurrency, concurrency);

        async Task<(SvnTreeEntry Entry, long? LastRev)> LoadLastRevAsync(SvnTreeEntry entry)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var r = await _svnlook.GetLastChangedRevisionAsync(repoLocalPath, entry.Path, headRevision, cancellationToken);
                return (entry, r);
            }
            catch
            {
                return (entry, null);
            }
            finally
            {
                gate.Release();
            }
        }

        var lastRevResults = await Task.WhenAll(entries.Select(LoadLastRevAsync));
        var lastRevByPath = lastRevResults
            .Where(x => x.LastRev is not null)
            .ToDictionary(x => x.Entry.Path, x => x.LastRev!.Value, StringComparer.Ordinal);

        var uniqueRevs = lastRevByPath.Values.Distinct().ToArray();
        var now = DateTimeOffset.UtcNow;

        async Task<(long Rev, DateTimeOffset? Date, string? Log, string? Author)> LoadRevInfoAsync(long rev)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var dt = await _svnlook.GetRevisionDateAsync(repoLocalPath, rev, cancellationToken);
                var log = await _svnlook.GetRevisionLogAsync(repoLocalPath, rev, cancellationToken);
                var author = await _svnlook.GetRevisionAuthorAsync(repoLocalPath, rev, cancellationToken);
                return (rev, dt, log, author);
            }
            catch
            {
                return (rev, null, null, null);
            }
            finally
            {
                gate.Release();
            }
        }

        var revInfos = await Task.WhenAll(uniqueRevs.Select(LoadRevInfoAsync));
        var revInfoByRev = revInfos.ToDictionary(
            x => x.Rev,
            x => (x.Date, x.Log, x.Author),
            comparer: EqualityComparer<long>.Default);

        return entries.Select(e =>
        {
            if (!lastRevByPath.TryGetValue(e.Path, out var lastRev))
            {
                return new TreeRow(e, null, null, null, null, null);
            }

            revInfoByRev.TryGetValue(lastRev, out var info);

            var age = info.Date is null ? null : IndexModel.FormatUpdatedAgo(info.Date.Value, now);
            var msg = CommitMessageFormatter.FirstLine(info.Log, 80);
            var author = string.IsNullOrWhiteSpace(info.Author) ? null : info.Author.Trim();

            return new TreeRow(e, msg, author, age, info.Date, lastRev);
        }).ToArray();
    }

    private async Task<IReadOnlyList<TreeRow>> LoadExternalRowsAsync(
        Repository repo,
        long revision,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SvnProperty> properties;
        try
        {
            properties = await _svnlook.GetPropertiesAsync(repo.LocalPath, Path, revision, cancellationToken);
        }
        catch
        {
            return Array.Empty<TreeRow>();
        }

        var externalsProperty = properties.FirstOrDefault(p =>
            string.Equals(p.Name, "svn:externals", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(externalsProperty?.Value))
        {
            return Array.Empty<TreeRow>();
        }

        var knownRepositories = _repos.List();
        var rows = new List<TreeRow>();
        foreach (var external in SvnExternalDefinitionParser.Parse(Path, externalsProperty.Value))
        {
            var link = TryBuildExternalLink(repo.Name, Path, knownRepositories, external, SvnBaseUrl);
            if (link is null)
            {
                continue;
            }

            var virtualPath = external.ResolvedPath ?? CombineRepoPath(Path, link.DisplayName);
            var virtualEntry = new SvnTreeEntry("@" + link.DisplayName, virtualPath, true);
            var targetLabel = link.IsInternal
                ? $"{link.TargetRepoName}{(link.TargetPath == "/" ? "" : link.TargetPath)}"
                : link.ExternalHref ?? external.Url ?? external.RawDefinition;
            rows.Add(new TreeRow(
                virtualEntry,
                targetLabel,
                "svn:externals",
                null,
                null,
                link.Revision,
                link));
        }

        return rows
            .OrderBy(r => r.ExternalLink?.DisplayName ?? r.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TreeExternalLink? TryBuildExternalLink(
        string currentRepoName,
        string currentPath,
        IReadOnlyList<Repository> repositories,
        SvnExternalDefinition external,
        string? svnBaseUrl)
    {
        var displayName = GetExternalDisplayName(external);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var revision = TryParseRevision(external.Revision) ?? TryParseRevision(external.PegRevision);
        if (!string.IsNullOrWhiteSpace(external.Url))
        {
            var target = ResolveExternalTarget(currentRepoName, repositories, external.Url);
            if (target is not null)
            {
                return new TreeExternalLink(
                    displayName,
                    target.Value.RepoName,
                    target.Value.Path,
                    null,
                    revision,
                    external.RawDefinition);
            }
        }

        return new TreeExternalLink(
            displayName,
            null,
            null,
            BuildExternalHref(svnBaseUrl, currentRepoName, currentPath, external.Url),
            revision,
            external.RawDefinition);
    }

    private static (string RepoName, string Path)? ResolveExternalTarget(
        string currentRepoName,
        IReadOnlyList<Repository> repositories,
        string url)
    {
        var value = url.Trim().Replace('\\', '/');
        if (value.Length == 0)
        {
            return null;
        }

        if (value.StartsWith("^/", StringComparison.Ordinal))
        {
            var relative = value[2..];
            return ResolveRepositoryPath(currentRepoName, repositories, relative);
        }

        if (value.StartsWith("../", StringComparison.Ordinal) ||
            value.StartsWith("./", StringComparison.Ordinal))
        {
            return ResolveRepositoryPath(currentRepoName, repositories, value);
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            return ResolveRepositoryPath(null, repositories, value.TrimStart('/'));
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return ResolveRepositoryPath(null, repositories, Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')));
        }

        return null;
    }

    private static (string RepoName, string Path)? TryResolveInternalExternalTarget(
        string currentRepoName,
        IReadOnlyList<Repository> repositories,
        string svnBaseUrl,
        SvnExternalDefinition external)
    {
        if (string.IsNullOrWhiteSpace(external.Url))
        {
            return null;
        }

        var value = external.Url.Trim().Replace('\\', '/');
        if (value.Length == 0)
        {
            return null;
        }

        if (value.StartsWith("^/", StringComparison.Ordinal) ||
            value.StartsWith("../", StringComparison.Ordinal) ||
            value.StartsWith("./", StringComparison.Ordinal))
        {
            return ResolveExternalTarget(currentRepoName, repositories, value);
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            return ResolveExternalTarget(currentRepoName, repositories, value);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var externalUri) ||
            !Uri.TryCreate(svnBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        if (!string.Equals(externalUri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(externalUri.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var externalPath = externalUri.AbsolutePath;
        if (!externalPath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativePath = Uri.UnescapeDataString(externalPath[(basePath.Length + 1)..]);
        return ResolveRepositoryPath(null, repositories, relativePath);
    }

    private static (string RepoName, string Path)? ResolveRepositoryPath(
        string? currentRepoName,
        IReadOnlyList<Repository> repositories,
        string relativePath)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(currentRepoName))
        {
            segments.Add(currentRepoName);
        }

        foreach (var rawSegment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawSegment == ".")
            {
                continue;
            }

            if (rawSegment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(rawSegment);
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var repo = repositories.FirstOrDefault(r =>
                string.Equals(r.Name, segments[i], StringComparison.OrdinalIgnoreCase));
            if (repo is null)
            {
                continue;
            }

            var pathSegments = segments.Skip(i + 1).ToArray();
            var targetPath = pathSegments.Length == 0 ? "/" : "/" + string.Join("/", pathSegments);
            return (repo.Name, Normalize(targetPath));
        }

        return null;
    }

    private static IReadOnlyList<TreeRow> MergeRows(
        IReadOnlyList<TreeRow> entryRows,
        IReadOnlyList<TreeRow> externalRows)
    {
        if (externalRows.Count == 0)
        {
            return entryRows;
        }

        if (entryRows.Count == 0)
        {
            return externalRows;
        }

        var rows = new List<TreeRow>(externalRows.Count + entryRows.Count);
        rows.AddRange(externalRows);
        rows.AddRange(entryRows);
        return rows;
    }

    private static long? TryParseRevision(string? value) =>
        long.TryParse(value, out var revision) && revision > 0 ? revision : null;

    private static string GetLastPathSegment(string path)
    {
        var normalized = path.Trim().Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0)
        {
            return "";
        }

        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static string GetExternalDisplayName(SvnExternalDefinition external)
    {
        if (!string.IsNullOrWhiteSpace(external.TargetPath))
        {
            return GetLastPathSegment(external.TargetPath);
        }

        if (!string.IsNullOrWhiteSpace(external.Url))
        {
            return GetLastPathSegment(external.Url);
        }

        return GetLastPathSegment(external.RawDefinition);
    }

    private static string? BuildExternalHref(
        string? svnBaseUrl,
        string currentRepoName,
        string currentPath,
        string? externalUrl)
    {
        if (string.IsNullOrWhiteSpace(externalUrl))
        {
            return null;
        }

        var value = externalUrl.Trim().Replace('\\', '/');
        if (Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return value;
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return value;
        }

        var normalizedBase = string.IsNullOrWhiteSpace(svnBaseUrl)
            ? null
            : svnBaseUrl.Trim().TrimEnd('/');

        if (value.StartsWith("^/", StringComparison.Ordinal))
        {
            return normalizedBase is null ? value : normalizedBase + "/" + value[2..].TrimStart('/');
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            if (normalizedBase is not null && Uri.TryCreate(normalizedBase, UriKind.Absolute, out var baseUri))
            {
                return $"{baseUri.Scheme}://{baseUri.Authority}{value}";
            }

            return value;
        }

        if (value.StartsWith("../", StringComparison.Ordinal) ||
            value.StartsWith("./", StringComparison.Ordinal))
        {
            var currentUrl = SvnCheckoutUrl.Build(normalizedBase, currentRepoName, currentPath);
            if (!string.IsNullOrWhiteSpace(currentUrl) &&
                Uri.TryCreate(currentUrl.TrimEnd('/') + "/", UriKind.Absolute, out var currentUri) &&
                Uri.TryCreate(currentUri, value, out var resolvedUri))
            {
                return resolvedUri.ToString();
            }
        }

        return value;
    }

    private static string? GetExternalZipRelativePath(string exportRootPath, SvnExternalDefinition external)
    {
        if (string.IsNullOrWhiteSpace(external.ResolvedPath))
        {
            return null;
        }

        var root = Normalize(exportRootPath);
        var target = Normalize(external.ResolvedPath);
        string relativePath;
        if (root == "/")
        {
            relativePath = target.TrimStart('/');
        }
        else if (target.StartsWith(root + "/", StringComparison.Ordinal))
        {
            relativePath = target[(root.Length + 1)..];
        }
        else
        {
            return null;
        }

        relativePath = relativePath.Trim().Replace('\\', '/').Trim('/');
        if (relativePath.Length == 0 ||
            relativePath.Contains("../", StringComparison.Ordinal) ||
            relativePath.Contains("/..", StringComparison.Ordinal) ||
            relativePath == "..")
        {
            return null;
        }

        return relativePath;
    }

    private static string? GetSafeExportDestination(string exportDir, string relativePath)
    {
        var normalizedRelative = relativePath.Trim().Replace('\\', '/').Trim('/');
        if (normalizedRelative.Length == 0 ||
            normalizedRelative.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return null;
        }

        var root = System.IO.Path.GetFullPath(exportDir);
        var destination = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(root, normalizedRelative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? root
            : root + System.IO.Path.DirectorySeparatorChar;

        return destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? destination
            : null;
    }

    private static string CombineRepoPath(string baseDir, string rel)
    {
        if (baseDir == "/")
        {
            return "/" + rel.TrimStart('/');
        }

        return baseDir.TrimEnd('/') + "/" + rel.TrimStart('/');
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
            return RedirectToPage(new { repoName, path = RoutePath(Path) });
        }

        var normalizedTarget = Normalize(targetPath);
        if (normalizedTarget == "/")
        {
            FlashError = "Refusing to delete repository root.";
            return RedirectToPage(new { repoName, path = RoutePath(Path) });
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
            await _svnWriter.DeleteAsync(repo.LocalPath, normalizedTarget, message, User?.Identity?.Name, cancellationToken);
        }
        catch (Exception ex)
        {
            FlashError = ex.Message;
            return RedirectToPage(new { repoName, path = RoutePath(Path) });
        }

        Message = $"Deleted {System.IO.Path.GetFileName(normalizedTarget)}";
        return RedirectToPage(new { repoName, path = RoutePath(Path) });
    }

    public async Task<IActionResult> OnPostUploadAsync(
        string repoName,
        string? path,
        string mode,
        List<IFormFile> files,
        string commitMessage,
        CancellationToken cancellationToken)
    {
        RepoName = repoName;
        Path = Normalize(path);
        ParentPath = GetParent(Path);
        SvnBaseUrl = _settings.GetEffectiveSvnBaseUrl();

        if (files is null || files.Count == 0)
        {
            FlashError = "Select at least one file to upload.";
            return RedirectToPage(new { repoName, path = RoutePath(Path) });
        }

        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            FlashError = "Commit message is required.";
            return RedirectToPage(new { repoName, path = RoutePath(Path) });
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

        if (_access.GetAccess(userId.Value, repo.Id, Path) < AccessLevel.Write)
        {
            return Forbid();
        }

        long rev;
        try
        {
            rev = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
        }
        catch (Exception ex)
        {
            FlashError = ex.Message;
            return RedirectToPage(new { repoName, path = RoutePath(Path) });
        }

        var maxUploadBytes = _settings.GetEffectiveMaxUploadBytes();

        var totalBytes = files.Sum(f => (long)f.Length);
        if (totalBytes > maxUploadBytes)
        {
            FlashError = $"Upload is too large (>{maxUploadBytes} bytes).";
            return RedirectToPage(new { repoName, path = RoutePath(Path) });
        }

        foreach (var f in files)
        {
            if (f.Length > maxUploadBytes)
            {
                FlashError = $"File '{System.IO.Path.GetFileName(f.FileName)}' is too large (>{maxUploadBytes} bytes).";
                return RedirectToPage(new { repoName, path = RoutePath(Path) });
            }
        }

        var normalizedMode = (mode ?? "").Trim().ToLowerInvariant();
        if (normalizedMode is not ("files" or "folder"))
        {
            FlashError = "Invalid upload mode.";
            return RedirectToPage(new { repoName, path = RoutePath(Path) });
        }

        static string NormalizeUploadPath(string raw, bool allowSubdirs)
        {
            var p = (raw ?? "").Replace('\\', '/').Trim();
            p = p.TrimStart('/');

            if (!allowSubdirs)
            {
                return System.IO.Path.GetFileName(p);
            }

            while (p.Contains("//", StringComparison.Ordinal))
            {
                p = p.Replace("//", "/", StringComparison.Ordinal);
            }

            if (p.Contains("../", StringComparison.Ordinal) || p.Contains("/..", StringComparison.Ordinal) || p.StartsWith("..", StringComparison.Ordinal))
            {
                return "";
            }

            if (p.Contains(':'))
            {
                return "";
            }

            return p;
        }

        static bool IsSafeSegment(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            if (s.Contains('/') || s.Contains('\\'))
            {
                return false;
            }

            return !s.Contains("..", StringComparison.Ordinal);
        }

        static string CombineRepoPath(string baseDir, string rel)
        {
            if (string.IsNullOrWhiteSpace(rel))
            {
                return "";
            }

            if (baseDir == "/")
            {
                return "/" + rel.TrimStart('/');
            }

            return baseDir.TrimEnd('/') + "/" + rel.TrimStart('/');
        }

        var allowSubdirs = normalizedMode == "folder";
        var puts = new List<SvnPutFile>(files.Count);
        var dirsToCreate = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in files)
        {
            var relRaw = NormalizeUploadPath(f.FileName, allowSubdirs);
            if (string.IsNullOrWhiteSpace(relRaw))
            {
                FlashError = $"Invalid upload path: {f.FileName}";
                return RedirectToPage(new { repoName, path = RoutePath(Path) });
            }

            var segments = relRaw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                FlashError = $"Invalid upload path: {f.FileName}";
                return RedirectToPage(new { repoName, path = RoutePath(Path) });
            }

            if (segments.Any(s => !IsSafeSegment(s)))
            {
                FlashError = $"Invalid upload path: {f.FileName}";
                return RedirectToPage(new { repoName, path = RoutePath(Path) });
            }

            // Accumulate directory creation requests for folder uploads.
            if (allowSubdirs && segments.Length > 1)
            {
                var cur = "";
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    cur = cur.Length == 0 ? segments[i] : cur + "/" + segments[i];
                    var fullDir = CombineRepoPath(Path, cur);
                    dirsToCreate.Add(fullDir);
                }
            }

            var rel = string.Join("/", segments);
            var targetPath = CombineRepoPath(Path, rel);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                FlashError = $"Invalid upload path: {f.FileName}";
                return RedirectToPage(new { repoName, path = RoutePath(Path) });
            }

            if (_access.GetAccess(userId.Value, repo.Id, targetPath) < AccessLevel.Write)
            {
                return Forbid();
            }

            await using var ms = new MemoryStream((int)Math.Min(f.Length, int.MaxValue));
            await f.CopyToAsync(ms, cancellationToken);
            puts.Add(new SvnPutFile(targetPath, ms.ToArray()));
        }

        // Decide which directories actually need creation, by checking existing entries per-parent (cached).
        var mkdirList = new List<string>();
        if (dirsToCreate.Count != 0)
        {
            var cache = new Dictionary<string, IReadOnlyList<SvnTreeEntry>>(StringComparer.Ordinal);

            async Task<IReadOnlyList<SvnTreeEntry>> GetChildrenAsync(string parent)
            {
                if (cache.TryGetValue(parent, out var existing))
                {
                    return existing;
                }

                try
                {
                    var list = await _svnlook.ListTreeAsync(repo.LocalPath, parent, rev, cancellationToken);
                    cache[parent] = list;
                    return list;
                }
                catch (Exception ex)
                {
                    // When uploading folders, we may need to probe children of a directory that does not exist yet.
                    // Treat "path not found" as an empty listing.
                    if (ex.Message.Contains("E160013", StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("File not found", StringComparison.OrdinalIgnoreCase))
                    {
                        cache[parent] = Array.Empty<SvnTreeEntry>();
                        return cache[parent];
                    }

                    throw;
                }
            }

            foreach (var dir in dirsToCreate.OrderBy(p => p.Count(c => c == '/'), Comparer<int>.Default).ThenBy(p => p, StringComparer.Ordinal))
            {
                var parent = GetParent(dir);
                var name = dir.TrimEnd('/');
                name = name[(name.LastIndexOf('/') + 1)..];

                var children = await GetChildrenAsync(parent);
                var match = children.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
                if (match is not null)
                {
                    if (!match.IsDirectory)
                    {
                        FlashError = $"Cannot create folder '{dir}': a file with the same name already exists.";
                        return RedirectToPage(new { repoName, path = RoutePath(Path) });
                    }

                    continue; // already exists
                }

                mkdirList.Add(dir);

                // Nested directories should treat this new directory as existing (but empty) during planning.
                cache[dir] = Array.Empty<SvnTreeEntry>();

                // Update cache so nested dirs can see their parent as existing without extra svnlook calls.
                if (cache.TryGetValue(parent, out var cached))
                {
                    cache[parent] = cached.Concat([new SvnTreeEntry(name, dir, true)]).ToArray();
                }
            }
        }

        try
        {
            await _svnWriter.UploadAsync(repo.LocalPath, mkdirList, puts, commitMessage.Trim(), User?.Identity?.Name, cancellationToken);
        }
        catch (Exception ex)
        {
            FlashError = ex.Message;
            return RedirectToPage(new { repoName, path = RoutePath(Path) });
        }

        Message = $"Uploaded {puts.Count} file(s).";
        return RedirectToPage(new { repoName, path = RoutePath(Path) });
    }

    public async Task<IActionResult> OnGetZipAsync(string repoName, string? path, long? rev, CancellationToken cancellationToken)
    {
        RepoName = repoName;
        Path = Normalize(path);
        ParentPath = GetParent(Path);
        ViewRevision = rev;

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

        long effectiveRev;
        try
        {
            HeadRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            effectiveRev = ResolveRevision(rev, HeadRevision);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        if (Path != "/")
        {
            try
            {
                var parent = GetParent(Path);
                var entries = await _svnlook.ListTreeAsync(repo.LocalPath, parent, effectiveRev, cancellationToken);
                var isDir = entries.Any(e => string.Equals(e.Path, Path, StringComparison.Ordinal) && e.IsDirectory);
                if (!isDir)
                {
                    return BadRequest("Path is not a folder.");
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        var repoRootUri = new Uri(System.IO.Path.GetFullPath(repo.LocalPath) + System.IO.Path.DirectorySeparatorChar);
        var rel = NormalizeRepoRelativePath(Path);
        if (Path != "/" && string.IsNullOrWhiteSpace(rel))
        {
            return BadRequest("Invalid path.");
        }

        var targetUrl = Path == "/"
            ? repoRootUri.AbsoluteUri
            : new Uri(repoRootUri, rel).AbsoluteUri;

        var exportDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"svnhub-export-{Guid.NewGuid():N}");
        try
        {
            var export = await _runner.RunAsync(
                _options.SvnCommand,
                ["export", "--non-interactive", "--quiet", "--ignore-externals", "-r", effectiveRev.ToString(), targetUrl, exportDir],
                cancellationToken);

            if (!export.IsSuccess)
            {
                return BadRequest($"svn export failed (exit {export.ExitCode}): {export.StandardError}".Trim());
            }

            var externalNotes = new List<string>();
            await ExportZipExternalsAsync(repo, Path, effectiveRev, userId.Value, exportDir, externalNotes, cancellationToken);
            if (externalNotes.Count > 0)
            {
                var manifestPath = System.IO.Path.Combine(exportDir, "SVNHUB-EXTERNALS.txt");
                await System.IO.File.WriteAllLinesAsync(
                    manifestPath,
                    [
                        "SvnHub external export notes",
                        "",
                        ..externalNotes,
                    ],
                    cancellationToken);
            }

            var folderName = Path == "/"
                ? repoName
                : System.IO.Path.GetFileName(Path.TrimEnd('/'));

            var zipName = $"{folderName}-r{effectiveRev}.zip";

            Response.ContentType = "application/zip";
            Response.Headers.ContentDisposition = $"attachment; filename=\"{zipName}\"";

            using (var zipStream = new AsyncWriteStream(Response.Body, cancellationToken))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Add empty directories explicitly.
                foreach (var dir in Directory.EnumerateDirectories(exportDir, "*", SearchOption.AllDirectories))
                {
                    if (Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        continue;
                    }

                    var relDir = System.IO.Path.GetRelativePath(exportDir, dir).Replace('\\', '/').TrimEnd('/') + "/";
                    archive.CreateEntry(relDir);
                }

                foreach (var file in Directory.EnumerateFiles(exportDir, "*", SearchOption.AllDirectories))
                {
                    var relFile = System.IO.Path.GetRelativePath(exportDir, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(relFile, CompressionLevel.Fastest);
                    await using var input = System.IO.File.OpenRead(file);
                    await using var output = entry.Open();
                    await input.CopyToAsync(output, cancellationToken);
                }
            }

            await Response.Body.FlushAsync(cancellationToken);
            return new EmptyResult();
        }
        finally
        {
            try
            {
                if (Directory.Exists(exportDir))
                {
                    Directory.Delete(exportDir, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private async Task ExportZipExternalsAsync(
        Repository sourceRepo,
        string exportRootPath,
        long sourceRevision,
        Guid userId,
        string exportDir,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var directories = await GetDirectoriesForExternalScanAsync(sourceRepo, exportRootPath, sourceRevision, notes, cancellationToken);
        if (directories.Count == 0)
        {
            return;
        }

        var knownRepositories = _repos.List();
        var svnBaseUrl = _settings.GetEffectiveSvnBaseUrl();

        foreach (var directory in directories)
        {
            IReadOnlyList<SvnProperty> properties;
            try
            {
                properties = await _svnlook.GetPropertiesAsync(sourceRepo.LocalPath, directory, sourceRevision, cancellationToken);
            }
            catch (Exception ex)
            {
                notes.Add($"{directory}: could not read svn:externals properties: {ex.Message}");
                continue;
            }

            var externalsProperty = properties.FirstOrDefault(p =>
                string.Equals(p.Name, "svn:externals", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(externalsProperty?.Value))
            {
                continue;
            }

            foreach (var external in SvnExternalDefinitionParser.Parse(directory, externalsProperty.Value))
            {
                await ExportSingleZipExternalAsync(
                    sourceRepo,
                    exportRootPath,
                    knownRepositories,
                    svnBaseUrl,
                    external,
                    userId,
                    exportDir,
                    notes,
                    cancellationToken);
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetDirectoriesForExternalScanAsync(
        Repository sourceRepo,
        string exportRootPath,
        long sourceRevision,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var directories = new HashSet<string>(StringComparer.Ordinal)
        {
            Normalize(exportRootPath),
        };

        IReadOnlyList<SvnTreeEntry> entries;
        try
        {
            entries = await _svnlook.ListTreeRecursiveAsync(sourceRepo.LocalPath, exportRootPath, sourceRevision, cancellationToken);
        }
        catch (Exception ex)
        {
            notes.Add($"{exportRootPath}: could not scan nested directories for svn:externals: {ex.Message}");
            return directories.ToArray();
        }

        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                directories.Add(Normalize(entry.Path));
            }
        }

        return directories
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task ExportSingleZipExternalAsync(
        Repository sourceRepo,
        string exportRootPath,
        IReadOnlyList<Repository> knownRepositories,
        string svnBaseUrl,
        SvnExternalDefinition external,
        Guid userId,
        string exportDir,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var destinationRelativePath = GetExternalZipRelativePath(exportRootPath, external);
        if (destinationRelativePath is null)
        {
            notes.Add($"{external.RawDefinition}: skipped because the target path is outside the exported folder.");
            return;
        }

        var destinationPath = GetSafeExportDestination(exportDir, destinationRelativePath);
        if (destinationPath is null)
        {
            notes.Add($"{destinationRelativePath}: skipped because the target path is not safe for ZIP export.");
            return;
        }

        if (System.IO.File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            notes.Add($"{destinationRelativePath}: skipped because the destination already exists in the exported tree.");
            return;
        }

        var destinationParent = System.IO.Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationParent))
        {
            Directory.CreateDirectory(destinationParent);
        }

        Directory.CreateDirectory(destinationPath);

        var externalRevision = TryParseRevision(external.Revision) ?? TryParseRevision(external.PegRevision);
        var internalTarget = TryResolveInternalExternalTarget(sourceRepo.Name, knownRepositories, svnBaseUrl, external);
        if (internalTarget is not null)
        {
            await ExportInternalZipExternalAsync(
                internalTarget.Value,
                externalRevision,
                userId,
                destinationRelativePath,
                destinationPath,
                notes,
                cancellationToken);
            return;
        }

        await ExportPublicZipExternalAsync(
            sourceRepo,
            svnBaseUrl,
            external,
            externalRevision,
            destinationRelativePath,
            destinationPath,
            notes,
            cancellationToken);
    }

    private async Task ExportInternalZipExternalAsync(
        (string RepoName, string Path) target,
        long? externalRevision,
        Guid userId,
        string destinationRelativePath,
        string destinationPath,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var targetRepo = _repos.FindByName(target.RepoName);
        if (targetRepo is null || targetRepo.IsArchived)
        {
            notes.Add($"{destinationRelativePath}: skipped because target repository '{target.RepoName}' was not found.");
            return;
        }

        if (_access.GetAccess(userId, targetRepo.Id, target.Path) < AccessLevel.Read)
        {
            notes.Add($"{destinationRelativePath}: skipped because current user has no Read access to {targetRepo.Name}{target.Path}.");
            return;
        }

        long revision;
        try
        {
            revision = externalRevision ?? await _svnlook.GetYoungestRevisionAsync(targetRepo.LocalPath, cancellationToken);
        }
        catch (Exception ex)
        {
            notes.Add($"{destinationRelativePath}: skipped because target revision could not be resolved: {ex.Message}");
            return;
        }

        var repoRootUri = new Uri(System.IO.Path.GetFullPath(targetRepo.LocalPath) + System.IO.Path.DirectorySeparatorChar);
        var rel = NormalizeRepoRelativePath(target.Path);
        var targetUrl = target.Path == "/"
            ? repoRootUri.AbsoluteUri
            : new Uri(repoRootUri, rel).AbsoluteUri;

        await RunExternalExportAsync(
            targetUrl,
            revision,
            destinationPath,
            destinationRelativePath,
            notes,
            cancellationToken);
    }

    private async Task ExportPublicZipExternalAsync(
        Repository sourceRepo,
        string svnBaseUrl,
        SvnExternalDefinition external,
        long? externalRevision,
        string destinationRelativePath,
        string destinationPath,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var externalHref = BuildExternalHref(svnBaseUrl, sourceRepo.Name, external.ParentPath, external.Url);
        if (string.IsNullOrWhiteSpace(externalHref) ||
            !Uri.TryCreate(externalHref, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            notes.Add($"{destinationRelativePath}: skipped because only public http/https externals can be fetched anonymously.");
            return;
        }

        await RunExternalExportAsync(
            uri.ToString(),
            externalRevision,
            destinationPath,
            destinationRelativePath,
            notes,
            cancellationToken);
    }

    private async Task RunExternalExportAsync(
        string targetUrl,
        long? revision,
        string destinationPath,
        string destinationRelativePath,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "export",
            "--non-interactive",
            "--quiet",
            "--ignore-externals",
            "--force",
        };

        if (revision is not null)
        {
            args.AddRange(["-r", revision.Value.ToString()]);
        }

        args.Add(targetUrl);
        args.Add(destinationPath);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ExternalZipExportTimeout);

        try
        {
            var result = await _runner.RunAsync(_options.SvnCommand, args, timeout.Token);
            if (!result.IsSuccess)
            {
                notes.Add($"{destinationRelativePath}: external export failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            notes.Add($"{destinationRelativePath}: external export timed out after {ExternalZipExportTimeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex)
        {
            notes.Add($"{destinationRelativePath}: external export failed: {ex.Message}");
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

    private static string Normalize(string? path) => RepositoryPath.Normalize(path);

    private static string GetParent(string path) => RepositoryPath.GetParent(path);

    private static string? RoutePath(string? path) => RepositoryPath.ToRouteValue(path);

    private static string NormalizeRepoRelativePath(string path)
    {
        var p = path.Trim().Replace('\\', '/').TrimStart('/');

        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        if (p.Contains("../", StringComparison.Ordinal) || p.Contains("/..", StringComparison.Ordinal))
        {
            return "";
        }

        return p;
    }

    private sealed class AsyncWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationToken _ct;

        public AsyncWriteStream(Stream inner, CancellationToken ct)
        {
            _inner = inner;
            _ct = ct;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() =>
            _inner.FlushAsync(_ct).GetAwaiter().GetResult();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.WriteAsync(buffer.AsMemory(offset, count), _ct).GetAwaiter().GetResult();

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(rented);
                Write(rented, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(buffer, cancellationToken);
    }

    public sealed record TreeRow(
        SvnTreeEntry Entry,
        string? LastCommitMessage,
        string? LastChangedAuthor,
        string? LastChangedAge,
        DateTimeOffset? LastChangedAt,
        long? LastChangedRevision,
        TreeExternalLink? ExternalLink = null);

    public sealed record TreeExternalLink(
        string DisplayName,
        string? TargetRepoName,
        string? TargetPath,
        string? ExternalHref,
        long? Revision,
        string RawDefinition)
    {
        public bool IsInternal =>
            !string.IsNullOrWhiteSpace(TargetRepoName) &&
            !string.IsNullOrWhiteSpace(TargetPath);
    }
}
