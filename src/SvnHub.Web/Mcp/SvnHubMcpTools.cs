using System.ComponentModel;
using System.Globalization;
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
    private const int DefaultQueryLimit = 100;
    private const int MaxQueryLimit = 1_000;
    private const int DefaultQueryHistoryLimitPerRepository = 50;
    private const int MaxQueryHistoryLimitPerRepository = 500;

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

    [McpServerTool(
        Name = "svnhub_query",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Run a safe declarative query over repositories, recent commits, or directory trees visible to the authenticated SvnHub user.")]
    public static async Task<McpQueryResult> QueryAsync(
        [Description("Query object. from: repositories|commits|tree. where uses allow-listed fields and operators: eq, neq, contains, startsWith, endsWith, gt, gte, lt, lte, in, exists.")]
        McpQueryRequest query,
        RepositoryService repositories,
        AccessService access,
        ISvnLookClient svnlook,
        IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        if (query is null)
        {
            throw new InvalidOperationException("Query is required.");
        }

        NormalizeQuery(query);

        var from = NormalizeQueryToken(query.From);
        if (from is not ("repositories" or "commits" or "tree"))
        {
            throw new InvalidOperationException("Query from must be one of: repositories, commits, tree.");
        }

        ValidateQuery(from, query);

        var userId = GetCurrentUserId(httpContextAccessor);
        var warnings = new List<string>();
        var visibleRepositories = FilterRepositoriesForQuery(
            repositories.List().Where(r => access.GetAccess(userId, r.Id, "/") >= AccessLevel.Read),
            query.Scan)
            .ToArray();

        var rows = from switch
        {
            "repositories" => await BuildRepositoryQueryRowsAsync(visibleRepositories, access, userId, svnlook, query, warnings, cancellationToken),
            "commits" => await BuildCommitQueryRowsAsync(visibleRepositories, access, userId, svnlook, query, warnings, cancellationToken),
            "tree" => await BuildTreeQueryRowsAsync(visibleRepositories, access, userId, svnlook, query, warnings, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported query source."),
        };

        var scannedRows = rows.Count;
        var matchedRows = rows
            .Where(row => MatchesAllConditions(row, query.Where))
            .ToList();

        if (query.GroupBy.Count > 0)
        {
            matchedRows = GroupRows(matchedRows, query.GroupBy);
        }

        matchedRows = OrderRows(matchedRows, query.OrderBy);

        var limit = Math.Clamp(query.Limit ?? DefaultQueryLimit, 1, MaxQueryLimit);
        var truncated = matchedRows.Count > limit;
        var selectedRows = matchedRows
            .Take(limit)
            .Select(row => SelectRow(row, query.Select, from, query.GroupBy.Count > 0))
            .ToArray();

        return new McpQueryResult(
            from,
            visibleRepositories.Length,
            scannedRows,
            matchedRows.Count,
            truncated,
            warnings,
            selectedRows);
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

    private static IEnumerable<Repository> FilterRepositoriesForQuery(
        IEnumerable<Repository> repositories,
        McpQueryScan? scan)
    {
        if (scan?.RepositoryNames is { Count: > 0 } names)
        {
            var allowed = names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            repositories = repositories.Where(r => allowed.Contains(r.Name));
        }

        if (!string.IsNullOrWhiteSpace(scan?.RepositoryNameContains))
        {
            repositories = repositories.Where(r =>
                r.Name.Contains(scan.RepositoryNameContains, StringComparison.OrdinalIgnoreCase));
        }

        return repositories;
    }

    private static async Task<List<Dictionary<string, object?>>> BuildRepositoryQueryRowsAsync(
        IReadOnlyList<Repository> repositories,
        AccessService access,
        Guid userId,
        ISvnLookClient svnlook,
        McpQueryRequest query,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var includeHead = QueryReferencesPrefix(query, "head.");
        var rows = new List<Dictionary<string, object?>>(repositories.Count);

        foreach (var repo in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = CreateRepositoryRow(repo, access.GetAccess(userId, repo.Id, "/"));
            if (includeHead)
            {
                try
                {
                    var head = await LoadRevisionInfoAsync(repo.LocalPath, null, svnlook, cancellationToken);
                    AddHeadFields(row, head);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to load HEAD for repository '{repo.Name}': {ex.Message}");
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    private static async Task<List<Dictionary<string, object?>>> BuildCommitQueryRowsAsync(
        IReadOnlyList<Repository> repositories,
        AccessService access,
        Guid userId,
        ISvnLookClient svnlook,
        McpQueryRequest query,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var path = NormalizePath(query.Scan?.Path);
        var take = Math.Clamp(
            query.Scan?.HistoryLimitPerRepository ?? DefaultQueryHistoryLimitPerRepository,
            1,
            MaxQueryHistoryLimitPerRepository);

        var rows = new List<Dictionary<string, object?>>();

        foreach (var repo in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var headRevision = await svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
                var history = await svnlook.GetHistoryAsync(repo.LocalPath, path, headRevision, take, cancellationToken);
                var revisionCache = new Dictionary<long, RevisionInfo>();

                foreach (var entry in history)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!revisionCache.TryGetValue(entry.Revision, out var info))
                    {
                        info = await LoadRevisionInfoAsync(repo.LocalPath, entry.Revision, svnlook, cancellationToken);
                        revisionCache[entry.Revision] = info;
                    }

                    var row = CreateRepositoryRow(repo, access.GetAccess(userId, repo.Id, "/"));
                    row["commit.revision"] = entry.Revision;
                    row["commit.author"] = info.Author;
                    row["commit.date"] = info.Date;
                    row["commit.message"] = info.Message;
                    row["commit.path"] = entry.Path;
                    rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to scan commits for repository '{repo.Name}': {ex.Message}");
            }
        }

        return rows;
    }

    private static void NormalizeQuery(McpQueryRequest query)
    {
        query.From = string.IsNullOrWhiteSpace(query.From) ? "repositories" : query.From.Trim();
        query.Scan ??= new McpQueryScan();
        query.Scan.RepositoryNames ??= [];
        query.Where ??= [];
        query.GroupBy ??= [];
        query.Select ??= [];
        query.OrderBy ??= [];

        foreach (var condition in query.Where)
        {
            condition.Values ??= [];
        }
    }

    private static async Task<List<Dictionary<string, object?>>> BuildTreeQueryRowsAsync(
        IReadOnlyList<Repository> repositories,
        AccessService access,
        Guid userId,
        ISvnLookClient svnlook,
        McpQueryRequest query,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var path = NormalizePath(query.Scan?.Path);
        if (query.Scan?.Depth is > 1)
        {
            warnings.Add("tree queries currently scan one directory level; scan.depth greater than 1 was ignored.");
        }

        var rows = new List<Dictionary<string, object?>>();

        foreach (var repo in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (access.GetAccess(userId, repo.Id, path) < AccessLevel.Read)
            {
                continue;
            }

            try
            {
                var head = await svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
                var revision = ResolveRevision(query.Scan?.Revision, head);
                var entries = await svnlook.ListTreeAsync(repo.LocalPath, path, revision, cancellationToken);

                foreach (var entry in entries)
                {
                    if (access.GetAccess(userId, repo.Id, entry.Path) < AccessLevel.Read)
                    {
                        continue;
                    }

                    var row = CreateRepositoryRow(repo, access.GetAccess(userId, repo.Id, entry.Path));
                    row["tree.revision"] = revision;
                    row["entry.name"] = entry.Name;
                    row["entry.path"] = entry.Path;
                    row["entry.extension"] = entry.IsDirectory ? null : GetExtensionWithoutDot(entry.Name);
                    row["entry.isDirectory"] = entry.IsDirectory;
                    rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to scan tree for repository '{repo.Name}': {ex.Message}");
            }
        }

        return rows;
    }

    private static Dictionary<string, object?> CreateRepositoryRow(Repository repo, AccessLevel? RootAccess)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["repository.name"] = repo.Name,
            ["repository.createdAt"] = repo.CreatedAt,
            ["repository.rootAccess"] = RootAccess?.ToString(),
            ["repository.authenticatedDefaultAccess"] = repo.AuthenticatedDefaultAccess?.ToString(),
        };

        return row;
    }

    private static async Task<RevisionInfo> LoadRevisionInfoAsync(
        string repoLocalPath,
        long? revision,
        ISvnLookClient svnlook,
        CancellationToken cancellationToken)
    {
        var effectiveRevision = revision ?? await svnlook.GetYoungestRevisionAsync(repoLocalPath, cancellationToken);
        var date = await svnlook.GetRevisionDateAsync(repoLocalPath, effectiveRevision, cancellationToken);
        var author = NullIfWhiteSpace(await svnlook.GetRevisionAuthorAsync(repoLocalPath, effectiveRevision, cancellationToken));
        var message = FirstLineOrNull(await svnlook.GetRevisionLogAsync(repoLocalPath, effectiveRevision, cancellationToken));
        return new RevisionInfo(effectiveRevision, date, author, message);
    }

    private static void AddHeadFields(Dictionary<string, object?> row, RevisionInfo info)
    {
        row["head.revision"] = info.Revision;
        row["head.author"] = info.Author;
        row["head.date"] = info.Date;
        row["head.message"] = info.Message;
    }

    private static bool MatchesAllConditions(
        Dictionary<string, object?> row,
        IReadOnlyList<McpQueryCondition> conditions) =>
        conditions.All(condition => MatchesCondition(row, condition));

    private static bool MatchesCondition(Dictionary<string, object?> row, McpQueryCondition condition)
    {
        var field = NormalizeField(condition.Field);
        var op = NormalizeQueryToken(condition.Op);

        row.TryGetValue(field, out var actual);

        return op switch
        {
            "exists" => actual is not null,
            "eq" => CompareValues(actual, condition.Value) == 0,
            "neq" => CompareValues(actual, condition.Value) != 0,
            "contains" => ContainsValue(actual, condition.Value),
            "startswith" => StartsOrEndsWith(actual, condition.Value, starts: true),
            "endswith" => StartsOrEndsWith(actual, condition.Value, starts: false),
            "gt" => CompareValues(actual, condition.Value) > 0,
            "gte" => CompareValues(actual, condition.Value) >= 0,
            "lt" => CompareValues(actual, condition.Value) < 0,
            "lte" => CompareValues(actual, condition.Value) <= 0,
            "in" => condition.Values.Any(v => CompareValues(actual, v) == 0),
            _ => throw new InvalidOperationException($"Unsupported query operator: {condition.Op}"),
        };
    }

    private static int CompareValues(object? actual, string? expected)
    {
        if (actual is null)
        {
            return string.IsNullOrWhiteSpace(expected) ? 0 : -1;
        }

        if (actual is DateTimeOffset actualDate)
        {
            if (!DateTimeOffset.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expectedDate))
            {
                return 1;
            }

            return actualDate.CompareTo(expectedDate);
        }

        if (actual is long actualLong)
        {
            return long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedLong)
                ? actualLong.CompareTo(expectedLong)
                : 1;
        }

        if (actual is int actualInt)
        {
            return int.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedInt)
                ? actualInt.CompareTo(expectedInt)
                : 1;
        }

        if (actual is bool actualBool)
        {
            return bool.TryParse(expected, out var expectedBool)
                ? actualBool.CompareTo(expectedBool)
                : 1;
        }

        return string.Compare(
            Convert.ToString(actual, CultureInfo.InvariantCulture),
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsValue(object? actual, string? expected) =>
        actual is not null &&
        !string.IsNullOrEmpty(expected) &&
        Convert.ToString(actual, CultureInfo.InvariantCulture)?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static bool StartsOrEndsWith(object? actual, string? expected, bool starts)
    {
        if (actual is null || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        var text = Convert.ToString(actual, CultureInfo.InvariantCulture) ?? "";
        return starts
            ? text.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
            : text.EndsWith(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static List<Dictionary<string, object?>> GroupRows(
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<string> groupBy)
    {
        var groups = rows.GroupBy(row =>
            string.Join("\u001f", groupBy.Select(field => Convert.ToString(GetRowValue(row, field), CultureInfo.InvariantCulture) ?? "")));

        var result = new List<Dictionary<string, object?>>();
        foreach (var group in groups)
        {
            var first = group.First();
            var grouped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in groupBy)
            {
                grouped[NormalizeField(field)] = GetRowValue(first, field);
            }

            grouped["count"] = group.Count();

            var latest = group
                .OrderByDescending(GetBestDate)
                .ThenByDescending(GetBestRevision)
                .First();

            CopyLatestFields(latest, grouped, "commit.");
            CopyLatestFields(latest, grouped, "head.");
            result.Add(grouped);
        }

        return result;
    }

    private static void CopyLatestFields(
        Dictionary<string, object?> source,
        Dictionary<string, object?> destination,
        string prefix)
    {
        foreach (var (key, value) in source.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            destination["latest." + key] = value;
        }
    }

    private static List<Dictionary<string, object?>> OrderRows(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<McpQueryOrder> orderBy)
    {
        if (orderBy.Count == 0)
        {
            return rows;
        }

        IOrderedEnumerable<Dictionary<string, object?>>? ordered = null;
        foreach (var order in orderBy)
        {
            var field = NormalizeField(order.Field);
            var descending = string.Equals(order.Direction, "desc", StringComparison.OrdinalIgnoreCase);
            Func<Dictionary<string, object?>, object?> keySelector = row => GetRowValue(row, field);

            ordered = ordered is null
                ? (descending ? rows.OrderByDescending(keySelector, QueryValueComparer.Instance) : rows.OrderBy(keySelector, QueryValueComparer.Instance))
                : (descending ? ordered.ThenByDescending(keySelector, QueryValueComparer.Instance) : ordered.ThenBy(keySelector, QueryValueComparer.Instance));
        }

        return ordered?.ToList() ?? rows;
    }

    private static Dictionary<string, object?> SelectRow(
        Dictionary<string, object?> row,
        IReadOnlyList<string> select,
        string from,
        bool isGrouped)
    {
        if (select.Count == 0)
        {
            select = GetDefaultSelect(from, row, isGrouped);
        }

        if (select.Any(s => string.Equals(s, "*", StringComparison.Ordinal)))
        {
            return new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in select)
        {
            var normalized = NormalizeField(field);
            result[normalized] = GetRowValue(row, normalized);
        }

        return result;
    }

    private static IReadOnlyList<string> GetDefaultSelect(string from, Dictionary<string, object?> row, bool isGrouped)
    {
        if (isGrouped)
        {
            return row.Keys.ToArray();
        }

        if (from == "commits")
        {
            return ["repository.name", "commit.revision", "commit.author", "commit.date", "commit.message", "commit.path"];
        }

        if (from == "tree")
        {
            return ["repository.name", "tree.revision", "entry.name", "entry.path", "entry.extension", "entry.isDirectory"];
        }

        return row.ContainsKey("head.revision")
            ? ["repository.name", "repository.createdAt", "repository.rootAccess", "head.revision", "head.author", "head.date", "head.message"]
            : ["repository.name", "repository.createdAt", "repository.rootAccess", "repository.authenticatedDefaultAccess"];
    }

    private static object? GetRowValue(Dictionary<string, object?> row, string field) =>
        row.TryGetValue(NormalizeField(field), out var value) ? value : null;

    private static DateTimeOffset? GetBestDate(Dictionary<string, object?> row)
    {
        if (row.TryGetValue("commit.date", out var commitDate) && commitDate is DateTimeOffset c)
        {
            return c;
        }

        if (row.TryGetValue("head.date", out var headDate) && headDate is DateTimeOffset h)
        {
            return h;
        }

        return null;
    }

    private static long GetBestRevision(Dictionary<string, object?> row)
    {
        if (row.TryGetValue("commit.revision", out var commitRevision) && commitRevision is long c)
        {
            return c;
        }

        if (row.TryGetValue("head.revision", out var headRevision) && headRevision is long h)
        {
            return h;
        }

        return 0;
    }

    private static void ValidateQuery(string from, McpQueryRequest query)
    {
        foreach (var condition in query.Where)
        {
            ValidateField(from, condition.Field, allowAggregateFields: false);
            var op = NormalizeQueryToken(condition.Op);
            if (op is not ("eq" or "neq" or "contains" or "startswith" or "endswith" or "gt" or "gte" or "lt" or "lte" or "in" or "exists"))
            {
                throw new InvalidOperationException($"Unsupported query operator: {condition.Op}");
            }

            if (op == "in" && condition.Values.Count == 0)
            {
                throw new InvalidOperationException("Operator 'in' requires values.");
            }
        }

        foreach (var field in query.GroupBy)
        {
            ValidateField(from, field, allowAggregateFields: false);
        }

        var grouped = query.GroupBy.Count > 0;
        foreach (var field in query.Select.Where(f => f != "*"))
        {
            ValidateField(from, field, allowAggregateFields: grouped);
        }

        foreach (var order in query.OrderBy)
        {
            ValidateField(from, order.Field, allowAggregateFields: grouped);
            if (!string.IsNullOrWhiteSpace(order.Direction) &&
                !string.Equals(order.Direction, "asc", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(order.Direction, "desc", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Query order direction must be asc or desc.");
            }
        }
    }

    private static void ValidateField(string from, string field, bool allowAggregateFields)
    {
        var normalized = NormalizeField(field);
        if (allowAggregateFields && (normalized == "count" || normalized.StartsWith("latest.", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!IsAllowedField(from, normalized))
        {
            throw new InvalidOperationException($"Field '{field}' is not allowed for query source '{from}'.");
        }
    }

    private static bool IsAllowedField(string from, string field)
    {
        if (field is "repository.name" or "repository.createdat" or "repository.rootaccess" or "repository.authenticateddefaultaccess")
        {
            return true;
        }

        return from switch
        {
            "repositories" => field is "head.revision" or "head.author" or "head.date" or "head.message",
            "commits" => field is "commit.revision" or "commit.author" or "commit.date" or "commit.message" or "commit.path",
            "tree" => field is "tree.revision" or "entry.name" or "entry.path" or "entry.extension" or "entry.isdirectory",
            _ => false,
        };
    }

    private static bool QueryReferencesPrefix(McpQueryRequest query, string prefix)
    {
        bool HasPrefix(string? field) =>
            !string.IsNullOrWhiteSpace(field) &&
            NormalizeField(field).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        return query.Where.Any(c => HasPrefix(c.Field)) ||
               query.Select.Any(HasPrefix) ||
               query.GroupBy.Any(HasPrefix) ||
               query.OrderBy.Any(o => HasPrefix(o.Field));
    }

    private static string NormalizeQueryToken(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeField(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string? GetExtensionWithoutDot(string name)
    {
        var extension = Path.GetExtension(name);
        return string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.').ToLowerInvariant();
    }

    private sealed record RevisionInfo(long Revision, DateTimeOffset Date, string? Author, string? Message);

    private sealed class QueryValueComparer : IComparer<object?>
    {
        public static QueryValueComparer Instance { get; } = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            if (x is DateTimeOffset dx && y is DateTimeOffset dy)
            {
                return dx.CompareTo(dy);
            }

            if (x is IComparable comparable && x.GetType() == y.GetType())
            {
                return comparable.CompareTo(y);
            }

            return string.Compare(
                Convert.ToString(x, CultureInfo.InvariantCulture),
                Convert.ToString(y, CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
        }
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

public sealed class McpQueryRequest
{
    public string From { get; set; } = "repositories";
    public McpQueryScan? Scan { get; set; }
    public List<McpQueryCondition> Where { get; set; } = [];
    public List<string> GroupBy { get; set; } = [];
    public List<string> Select { get; set; } = [];
    public List<McpQueryOrder> OrderBy { get; set; } = [];
    public int? Limit { get; set; }
}

public sealed class McpQueryScan
{
    public List<string> RepositoryNames { get; set; } = [];
    public string? RepositoryNameContains { get; set; }
    public string? Path { get; set; }
    public long? Revision { get; set; }
    public int? Depth { get; set; }
    public int? HistoryLimitPerRepository { get; set; }
}

public sealed class McpQueryCondition
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = "eq";
    public string? Value { get; set; }
    public List<string> Values { get; set; } = [];
}

public sealed class McpQueryOrder
{
    public string Field { get; set; } = "";
    public string Direction { get; set; } = "asc";
}

public sealed record McpQueryResult(
    string From,
    int ScannedRepositories,
    int ScannedRows,
    int MatchedRows,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<Dictionary<string, object?>> Rows);
