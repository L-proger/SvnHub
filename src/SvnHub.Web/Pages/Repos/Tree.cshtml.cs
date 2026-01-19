using System.IO.Compression;
using System.Buffers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
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
    public IReadOnlyList<SvnTreeEntry> Entries { get; private set; } = [];
    public IReadOnlyList<TreeRow> Rows { get; private set; } = [];
    public ISet<string> DeletablePaths { get; private set; } = new HashSet<string>(StringComparer.Ordinal);
    public ISet<string> ZipPaths { get; private set; } = new HashSet<string>(StringComparer.Ordinal);
    public bool CanWriteHere { get; private set; }
    public bool CanWriteActions { get; private set; }
    public int DirectoryCount { get; private set; }
    public int FileCount { get; private set; }
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

    public async Task<IActionResult> OnGetAsync(string repoName, string? path, long? rev, CancellationToken cancellationToken)
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

        if (_access.GetAccess(userId.Value, repo.Id, Path) < AccessLevel.Read)
        {
            return Forbid();
        }

        CanWriteHere = _access.GetAccess(userId.Value, repo.Id, Path) >= AccessLevel.Write;
        CanWriteActions = CanWriteHere && rev is null;

        try
        {
            HeadRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            Revision = ResolveRevision(rev, HeadRevision);
            Entries = await _svnlook.ListTreeAsync(repo.LocalPath, Path, Revision, cancellationToken);
            DirectoryCount = Entries.Count(e => e.IsDirectory);
            FileCount = Entries.Count(e => !e.IsDirectory);
            Rows = await LoadRowsAsync(repo.LocalPath, Entries, Revision, cancellationToken);

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
                var readmeText = await _svnlook.CatAsync(repo.LocalPath, readme.Path, Revision, cancellationToken);
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
        catch (Exception ex)
        {
            Error = ex.Message;
            DirectoryCount = 0;
            FileCount = 0;
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
                return new TreeRow(e, null, null, null, null);
            }

            revInfoByRev.TryGetValue(lastRev, out var info);

            var age = info.Date is null ? null : IndexModel.FormatUpdatedAgo(info.Date.Value, now);
            var msg = FormatCommitMessage(info.Log);
            var author = string.IsNullOrWhiteSpace(info.Author) ? null : info.Author.Trim();

            return new TreeRow(e, msg, author, age, lastRev);
        }).ToArray();
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

        const int max = 80;
        return firstLine.Length <= max ? firstLine : firstLine[..max] + "…";
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
            await _svnWriter.DeleteAsync(repo.LocalPath, normalizedTarget, message, User?.Identity?.Name, cancellationToken);
        }
        catch (Exception ex)
        {
            FlashError = ex.Message;
            return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
        }

        Message = $"Deleted {System.IO.Path.GetFileName(normalizedTarget)}";
        return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
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
            return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
        }

        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            FlashError = "Commit message is required.";
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
            return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
        }

        var maxUploadBytes = _settings.GetEffectiveMaxUploadBytes();

        var totalBytes = files.Sum(f => (long)f.Length);
        if (totalBytes > maxUploadBytes)
        {
            FlashError = $"Upload is too large (>{maxUploadBytes} bytes).";
            return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
        }

        foreach (var f in files)
        {
            if (f.Length > maxUploadBytes)
            {
                FlashError = $"File '{System.IO.Path.GetFileName(f.FileName)}' is too large (>{maxUploadBytes} bytes).";
                return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
            }
        }

        var normalizedMode = (mode ?? "").Trim().ToLowerInvariant();
        if (normalizedMode is not ("files" or "folder"))
        {
            FlashError = "Invalid upload mode.";
            return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
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
                return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
            }

            var segments = relRaw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                FlashError = $"Invalid upload path: {f.FileName}";
                return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
            }

            if (segments.Any(s => !IsSafeSegment(s)))
            {
                FlashError = $"Invalid upload path: {f.FileName}";
                return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
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
                return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
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
                        return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
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
            return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
        }

        Message = $"Uploaded {puts.Count} file(s).";
        return RedirectToPage(new { repoName, path = Path == "/" ? null : Path });
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
                ["export", "--non-interactive", "--quiet", "-r", effectiveRev.ToString(), targetUrl, exportDir],
                cancellationToken);

            if (!export.IsSuccess)
            {
                return BadRequest($"svn export failed (exit {export.ExitCode}): {export.StandardError}".Trim());
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
        long? LastChangedRevision);
}
