using System.Globalization;
using SvnHub.App.Indexing;
using SvnHub.App.Support;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class RepositoryIndexQueryService
{
    private const int DefaultQueryLimit = 100;
    private const int MaxQueryLimit = 1_000;

    private readonly IRepositoryIndexStore _store;
    private readonly RepositoryService _repositories;
    private readonly AccessService _access;

    public RepositoryIndexQueryService(
        IRepositoryIndexStore store,
        RepositoryService repositories,
        AccessService access)
    {
        _store = store;
        _repositories = repositories;
        _access = access;
    }

    public async Task<RepositoryIndexQueryResult> QueryAsync(
        RepositoryIndexQueryRequest query,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
        {
            throw new InvalidOperationException("Query is required.");
        }

        string from;
        try
        {
            NormalizeQuery(query);

            from = NormalizeSource(query.From);
            if (from is not ("repositories" or "commits" or "changedpaths" or "tree" or "properties" or "externals"))
            {
                return QueryError(from, $"Query from must be one of: repositories, commits, changedPaths, tree, properties, externals. Received: '{query.From}'.");
            }

            NormalizeQueryFields(from, query);
            ValidateQuery(from, query);
        }
        catch (InvalidOperationException ex)
        {
            return QueryError(query.From, ex.Message);
        }

        var allRepositories = _repositories.List()
            .Where(r => _access.GetAccess(userId, r.Id, "/") >= AccessLevel.Read)
            .ToArray();

        var visibleRepositories = FilterRepositoriesForQuery(allRepositories, query.Scan)
            .ToArray();
        var repositoryById = visibleRepositories.ToDictionary(r => r.Id);
        var visibleRepositoryIds = repositoryById.Keys.ToHashSet();

        var status = await _store.GetStatusAsync(cancellationToken);
        var indexInfo = BuildIndexInfo(status, visibleRepositoryIds, from);
        var warnings = new List<string>();
        if (!indexInfo.Complete)
        {
            var parts = new List<string>();
            if (indexInfo.MissingRepositories > 0)
            {
                parts.Add($"{indexInfo.MissingRepositories} visible repositories are not indexed yet");
            }

            if (indexInfo.BehindRepositories > 0)
            {
                parts.Add(
                    $"index is behind HEAD for {indexInfo.BehindRepositories} visible repositories " +
                    $"({indexInfo.RemainingRevisions} revisions remaining)");
            }

            if (indexInfo.StaleSnapshotRepositories > 0)
            {
                parts.Add($"HEAD snapshot is stale or missing for {indexInfo.StaleSnapshotRepositories} visible repositories");
            }

            warnings.Add($"Index is incomplete: {string.Join("; ", parts)}. Results can be incomplete.");
        }

        if (status.Repositories.Any(r => visibleRepositoryIds.Contains(r.RepositoryId) && !string.IsNullOrWhiteSpace(r.LastError)))
        {
            warnings.Add("Some visible repositories have indexing errors; check Admin / Settings / Index status.");
        }

        var rows = from switch
        {
            "repositories" => await BuildRepositoryRowsAsync(repositoryById, userId, cancellationToken),
            "commits" => await BuildCommitRowsAsync(repositoryById, userId, cancellationToken),
            "changedpaths" => await BuildChangedPathRowsAsync(repositoryById, userId, cancellationToken),
            "tree" => await BuildTreeRowsAsync(repositoryById, userId, cancellationToken),
            "properties" => await BuildPropertyRowsAsync(repositoryById, userId, cancellationToken),
            "externals" => await BuildExternalRowsAsync(repositoryById, userId, cancellationToken),
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
        var offset = Math.Clamp(query.Offset ?? 0, 0, int.MaxValue);
        var nextOffset = (long)offset + limit;
        var truncated = matchedRows.Count > nextOffset;
        if (truncated)
        {
            warnings.Add(
                $"Result page is truncated: returned rows {offset + 1}-{Math.Min(nextOffset, matchedRows.Count)} of {matchedRows.Count}. " +
                $"Request the next page with offset={nextOffset} and limit up to {MaxQueryLimit}.");
        }

        var selectedRows = matchedRows
            .Skip(offset)
            .Take(limit)
            .Select(row => SelectRow(row, query.Select, from, query.GroupBy.Count > 0))
            .ToArray();

        return new RepositoryIndexQueryResult(
            DisplaySource(from),
            visibleRepositories.Length,
            scannedRows,
            matchedRows.Count,
            truncated,
            offset,
            limit,
            indexInfo,
            warnings,
            selectedRows);
    }

    private async Task<List<Dictionary<string, object?>>> BuildRepositoryRowsAsync(
        IReadOnlyDictionary<Guid, Repository> visibleRepositories,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var heads = await _store.ListRepositoryHeadsAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        foreach (var head in heads)
        {
            if (!visibleRepositories.TryGetValue(head.RepositoryId, out var repo))
            {
                continue;
            }

            var row = CreateBaseRow(
                repo,
                _access.GetAccess(userId, repo.Id, "/"),
                head.YoungestRevision,
                head.IndexedRevision,
                head.HeadTreeRevision,
                head.PropertiesRevision,
                head.ExternalsRevision,
                head.LastSuccessAt,
                head.LastError,
                head.IsMissing);
            row["latest.revision"] = head.IndexedRevision > 0 ? head.IndexedRevision : null;
            row["latest.author"] = head.Author;
            row["latest.date"] = head.Date;
            row["latest.message"] = FirstLineOrNull(head.Message);
            rows.Add(row);
        }

        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> BuildCommitRowsAsync(
        IReadOnlyDictionary<Guid, Repository> visibleRepositories,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var commits = await _store.ListCommitsAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        foreach (var commit in commits)
        {
            if (!visibleRepositories.TryGetValue(commit.RepositoryId, out var repo))
            {
                continue;
            }

            var visibleChangedPaths = commit.ChangedPaths
                .Where(p => _access.GetAccess(userId, repo.Id, p.Path) >= AccessLevel.Read)
                .ToArray();
            if (visibleChangedPaths.Length == 0)
            {
                continue;
            }

            var row = CreateBaseRow(
                repo,
                _access.GetAccess(userId, repo.Id, "/"),
                commit.YoungestRevision,
                commit.IndexedRevision,
                0,
                0,
                0,
                lastSuccessAt: null,
                lastError: null,
                isMissing: false);
            AddCommitFields(row, commit.Revision, commit.Author, commit.Date, commit.Message);
            AddChangedPathFields(row, "commit", visibleChangedPaths);
            rows.Add(row);
        }

        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> BuildChangedPathRowsAsync(
        IReadOnlyDictionary<Guid, Repository> visibleRepositories,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var changes = await _store.ListChangedPathsAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        foreach (var change in changes)
        {
            if (!visibleRepositories.TryGetValue(change.RepositoryId, out var repo))
            {
                continue;
            }

            if (_access.GetAccess(userId, repo.Id, change.Path) < AccessLevel.Read)
            {
                continue;
            }

            var row = CreateBaseRow(
                repo,
                _access.GetAccess(userId, repo.Id, "/"),
                change.YoungestRevision,
                change.IndexedRevision,
                0,
                0,
                0,
                lastSuccessAt: null,
                lastError: null,
                isMissing: false);
            AddCommitFields(row, change.Revision, change.Author, change.Date, change.Message);
            row["change.action"] = change.Action;
            row["change.path"] = change.Path;
            rows.Add(row);
        }

        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> BuildTreeRowsAsync(
        IReadOnlyDictionary<Guid, Repository> visibleRepositories,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entries = await _store.ListHeadTreeEntriesAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        foreach (var entry in entries)
        {
            if (!visibleRepositories.TryGetValue(entry.RepositoryId, out var repo))
            {
                continue;
            }

            if (_access.GetAccess(userId, repo.Id, entry.Path) < AccessLevel.Read)
            {
                continue;
            }

            var row = CreateBaseRow(
                repo,
                _access.GetAccess(userId, repo.Id, "/"),
                entry.YoungestRevision,
                entry.IndexedRevision,
                entry.HeadTreeRevision,
                0,
                0,
                lastSuccessAt: null,
                lastError: null,
                isMissing: false);
            row["tree.revision"] = entry.HeadTreeRevision;
            row["tree.path"] = entry.Path;
            row["tree.name"] = entry.Name;
            row["tree.extension"] = entry.Extension;
            row["tree.isDirectory"] = entry.IsDirectory;
            rows.Add(row);
        }

        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> BuildExternalRowsAsync(
        IReadOnlyDictionary<Guid, Repository> visibleRepositories,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var externals = await _store.ListHeadExternalsAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        foreach (var external in externals)
        {
            if (!visibleRepositories.TryGetValue(external.RepositoryId, out var repo))
            {
                continue;
            }

            var accessPath = external.ResolvedPath ?? external.ParentPath;
            if (_access.GetAccess(userId, repo.Id, accessPath) < AccessLevel.Read)
            {
                continue;
            }

            var row = CreateBaseRow(
                repo,
                _access.GetAccess(userId, repo.Id, "/"),
                external.YoungestRevision,
                external.IndexedRevision,
                0,
                0,
                external.ExternalsRevision,
                lastSuccessAt: null,
                lastError: null,
                isMissing: false);
            row["external.snapshotRevision"] = external.ExternalsRevision;
            row["external.parentPath"] = external.ParentPath;
            row["external.targetPath"] = external.TargetPath;
            row["external.resolvedPath"] = external.ResolvedPath;
            row["external.url"] = external.Url;
            row["external.revision"] = external.Revision;
            row["external.pegRevision"] = external.PegRevision;
            row["external.isPinned"] = external.IsPinned;
            row["external.raw"] = external.RawDefinition;
            rows.Add(row);
        }

        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> BuildPropertyRowsAsync(
        IReadOnlyDictionary<Guid, Repository> visibleRepositories,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var properties = await _store.ListHeadPropertiesAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        foreach (var property in properties)
        {
            if (!visibleRepositories.TryGetValue(property.RepositoryId, out var repo))
            {
                continue;
            }

            if (_access.GetAccess(userId, repo.Id, property.Path) < AccessLevel.Read)
            {
                continue;
            }

            var row = CreateBaseRow(
                repo,
                _access.GetAccess(userId, repo.Id, "/"),
                property.YoungestRevision,
                property.IndexedRevision,
                0,
                property.PropertiesRevision,
                0,
                lastSuccessAt: null,
                lastError: null,
                isMissing: false);
            row["property.snapshotRevision"] = property.PropertiesRevision;
            row["property.path"] = property.Path;
            row["property.nodeKind"] = property.NodeKind;
            row["property.name"] = property.Name;
            row["property.value"] = property.Value;
            rows.Add(row);
        }

        return rows;
    }

    private static RepositoryIndexQueryIndexInfo BuildIndexInfo(
        RepositoryIndexStoreStatus status,
        IReadOnlySet<Guid> visibleRepositoryIds,
        string from)
    {
        var visibleRows = status.Repositories
            .Where(r => visibleRepositoryIds.Contains(r.RepositoryId) && !r.IsMissing)
            .ToArray();
        var missingRepositories = Math.Max(0, visibleRepositoryIds.Count - visibleRows.Length);
        var behindRepositories = visibleRows.Count(r => r.IndexedRevision < r.YoungestRevision);
        var remainingRevisions = visibleRows.Sum(r => Math.Max(0, r.YoungestRevision - r.IndexedRevision));
        var staleSnapshotRepositories = from switch
        {
            "tree" => visibleRows.Count(r => r.IndexedRevision >= r.YoungestRevision && r.HeadTreeRevision != r.IndexedRevision),
            "properties" => visibleRows.Count(r => r.IndexedRevision >= r.YoungestRevision && r.PropertiesRevision != r.IndexedRevision),
            "externals" => visibleRows.Count(r => r.IndexedRevision >= r.YoungestRevision && r.ExternalsRevision != r.IndexedRevision),
            _ => 0,
        };

        return new RepositoryIndexQueryIndexInfo(
            Complete: missingRepositories == 0 && behindRepositories == 0 && staleSnapshotRepositories == 0,
            MissingRepositories: missingRepositories,
            BehindRepositories: behindRepositories,
            RemainingRevisions: remainingRevisions,
            LastSuccessAt: status.LastSuccessAt,
            StaleSnapshotRepositories: staleSnapshotRepositories);
    }

    private Dictionary<string, object?> CreateBaseRow(
        Repository repo,
        AccessLevel rootAccess,
        long youngestRevision,
        long indexedRevision,
        long headTreeRevision,
        long propertiesRevision,
        long externalsRevision,
        DateTimeOffset? lastSuccessAt,
        string? lastError,
        bool isMissing)
    {
        var remaining = Math.Max(0, youngestRevision - indexedRevision);
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["repository.name"] = repo.Name,
            ["repository.createdAt"] = repo.CreatedAt,
            ["repository.labels"] = RepositoryLabels.Normalize(repo.Labels).ToArray(),
            ["repository.rootAccess"] = rootAccess.ToString(),
            ["repository.inheritedContentGrants"] = repo.IncludeInheritedContentGrants,
            ["repository.inheritedManagementGrants"] = repo.IncludeInheritedManagementGrants,
            ["indexed.headRevision"] = youngestRevision,
            ["indexed.revision"] = indexedRevision,
            ["indexed.headTreeRevision"] = headTreeRevision,
            ["indexed.propertiesRevision"] = propertiesRevision,
            ["indexed.externalsRevision"] = externalsRevision,
            ["indexed.remainingRevisions"] = remaining,
            ["indexed.complete"] = !isMissing && remaining == 0,
            ["indexed.lastSuccessAt"] = lastSuccessAt,
            ["indexed.lastError"] = lastError,
            ["indexed.isMissing"] = isMissing,
        };
    }

    private static void AddCommitFields(
        Dictionary<string, object?> row,
        long revision,
        string? author,
        DateTimeOffset date,
        string? message)
    {
        row["commit.revision"] = revision;
        row["commit.author"] = author;
        row["commit.date"] = date;
        row["commit.message"] = FirstLineOrNull(message);
    }

    private static void AddChangedPathFields(
        Dictionary<string, object?> row,
        string prefix,
        IReadOnlyList<SvnChangedPath> changedPaths)
    {
        row[$"{prefix}.changedPathCount"] = changedPaths.Count;
        row[$"{prefix}.changedPaths"] = changedPaths
            .Select(p => new RepositoryIndexQueryChangedPath(p.Action, p.Path))
            .ToArray();
    }

    private static IEnumerable<Repository> FilterRepositoriesForQuery(
        IEnumerable<Repository> repositories,
        RepositoryIndexQueryScan? scan)
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

    private static void NormalizeQuery(RepositoryIndexQueryRequest query)
    {
        query.From = string.IsNullOrWhiteSpace(query.From) ? "repositories" : query.From.Trim();
        query.Scan ??= new RepositoryIndexQueryScan();
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

    private static void NormalizeQueryFields(string from, RepositoryIndexQueryRequest query)
    {
        foreach (var condition in query.Where)
        {
            condition.Field = ResolveFieldAlias(from, condition.Field, allowAggregateFields: false);
            NormalizeConditionValues(condition);
        }

        for (var i = 0; i < query.GroupBy.Count; i++)
        {
            query.GroupBy[i] = ResolveFieldAlias(from, query.GroupBy[i], allowAggregateFields: false);
        }

        for (var i = 0; i < query.Select.Count; i++)
        {
            query.Select[i] = ResolveFieldAlias(from, query.Select[i], allowAggregateFields: query.GroupBy.Count > 0);
        }

        foreach (var order in query.OrderBy)
        {
            order.Field = ResolveFieldAlias(from, order.Field, allowAggregateFields: query.GroupBy.Count > 0);
        }
    }

    private static string ResolveFieldAlias(string from, string? field, bool allowAggregateFields)
    {
        var normalized = NormalizeField(field);
        if (normalized == "*" ||
            (allowAggregateFields && (normalized == "count" || normalized.StartsWith("latest.", StringComparison.OrdinalIgnoreCase))) ||
            IsAllowedField(from, normalized))
        {
            return normalized;
        }

        if (normalized is "label" or "labels" or "repositorylabel" or "repositorylabels")
        {
            return "repository.labels";
        }

        return from switch
        {
            "repositories" => normalized switch
            {
                "name" or "repositoryname" or "repo" or "reponame" => "repository.name",
                "createdat" or "created" => "repository.createdAt",
                "rootaccess" or "access" => "repository.rootAccess",
                "inheritedcontentgrants" => "repository.inheritedContentGrants",
                "inheritedmanagementgrants" => "repository.inheritedManagementGrants",
                "revision" or "latestrevision" => "latest.revision",
                "author" or "latestauthor" => "latest.author",
                "date" or "latestdate" => "latest.date",
                "message" or "latestmessage" => "latest.message",
                "headrevision" => "indexed.headRevision",
                "indexedrevision" => "indexed.revision",
                "headtreerevision" => "indexed.headTreeRevision",
                "propertiesrevision" => "indexed.propertiesRevision",
                "externalsrevision" => "indexed.externalsRevision",
                "remainingrevisions" => "indexed.remainingRevisions",
                "complete" => "indexed.complete",
                _ => normalized,
            },
            "commits" => normalized switch
            {
                "name" or "repositoryname" or "repo" or "reponame" => "repository.name",
                "createdat" or "created" => "repository.createdAt",
                "rootaccess" or "access" => "repository.rootAccess",
                "inheritedcontentgrants" => "repository.inheritedContentGrants",
                "inheritedmanagementgrants" => "repository.inheritedManagementGrants",
                "revision" or "commitrevision" => "commit.revision",
                "author" or "commitauthor" => "commit.author",
                "date" or "commitdate" => "commit.date",
                "message" or "commitmessage" => "commit.message",
                "changedpaths" or "commitchangedpaths" => "commit.changedPaths",
                "changedpathcount" or "commitchangedpathcount" => "commit.changedPathCount",
                _ => normalized,
            },
            "changedpaths" => normalized switch
            {
                "name" or "repositoryname" or "repo" or "reponame" => "repository.name",
                "createdat" or "created" => "repository.createdAt",
                "rootaccess" or "access" => "repository.rootAccess",
                "inheritedcontentgrants" => "repository.inheritedContentGrants",
                "inheritedmanagementgrants" => "repository.inheritedManagementGrants",
                "revision" or "commitrevision" => "commit.revision",
                "author" or "commitauthor" => "commit.author",
                "date" or "commitdate" => "commit.date",
                "message" or "commitmessage" => "commit.message",
                "path" or "changepath" => "change.path",
                "action" or "changeaction" => "change.action",
                _ => normalized,
            },
            "tree" => normalized switch
            {
                "repositoryname" or "repo" or "reponame" => "repository.name",
                "createdat" or "created" => "repository.createdAt",
                "rootaccess" or "access" => "repository.rootAccess",
                "inheritedcontentgrants" => "repository.inheritedContentGrants",
                "inheritedmanagementgrants" => "repository.inheritedManagementGrants",
                "revision" or "snapshotrevision" or "treerevision" => "tree.revision",
                "path" or "treepath" => "tree.path",
                "name" or "filename" or "entryname" or "treename" => "tree.name",
                "extension" or "ext" => "tree.extension",
                "isdirectory" or "directory" or "isdir" => "tree.isDirectory",
                "headrevision" => "indexed.headRevision",
                "indexedrevision" => "indexed.revision",
                "headtreerevision" => "indexed.headTreeRevision",
                "propertiesrevision" => "indexed.propertiesRevision",
                "remainingrevisions" => "indexed.remainingRevisions",
                "complete" => "indexed.complete",
                _ => normalized,
            },
            "externals" => normalized switch
            {
                "repositoryname" or "repo" or "reponame" => "repository.name",
                "createdat" or "created" => "repository.createdAt",
                "rootaccess" or "access" => "repository.rootAccess",
                "inheritedcontentgrants" => "repository.inheritedContentGrants",
                "inheritedmanagementgrants" => "repository.inheritedManagementGrants",
                "snapshotrevision" or "headrevision" => "external.snapshotRevision",
                "parent" or "parentpath" => "external.parentPath",
                "name" or "target" or "targetpath" or "externalname" => "external.targetPath",
                "path" or "resolvedpath" or "externalpath" => "external.resolvedPath",
                "url" or "externalurl" => "external.url",
                "revision" or "externalrevision" => "external.revision",
                "pegrevision" => "external.pegRevision",
                "ispinned" or "pinned" => "external.isPinned",
                "raw" or "definition" or "rawdefinition" => "external.raw",
                "indexedrevision" => "indexed.revision",
                "externalsrevision" => "indexed.externalsRevision",
                "remainingrevisions" => "indexed.remainingRevisions",
                "complete" => "indexed.complete",
                _ => normalized,
            },
            "properties" => normalized switch
            {
                "repositoryname" or "repo" or "reponame" => "repository.name",
                "createdat" or "created" => "repository.createdAt",
                "rootaccess" or "access" => "repository.rootAccess",
                "inheritedcontentgrants" => "repository.inheritedContentGrants",
                "inheritedmanagementgrants" => "repository.inheritedManagementGrants",
                "snapshotrevision" or "headrevision" => "property.snapshotRevision",
                "path" or "propertypath" => "property.path",
                "nodekind" or "kind" => "property.nodeKind",
                "name" or "propertyname" => "property.name",
                "value" or "propertyvalue" => "property.value",
                "indexedrevision" => "indexed.revision",
                "propertiesrevision" => "indexed.propertiesRevision",
                "externalsrevision" => "indexed.externalsRevision",
                "remainingrevisions" => "indexed.remainingRevisions",
                "complete" => "indexed.complete",
                _ => normalized,
            },
            _ => normalized,
        };
    }

    private static void NormalizeConditionValues(RepositoryIndexQueryCondition condition)
    {
        if (!string.Equals(condition.Field, "tree.extension", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var op = NormalizeQueryToken(condition.Op);
        if (op is not ("eq" or "neq" or "in"))
        {
            return;
        }

        condition.Value = NormalizeExtensionFilterValue(condition.Value);
        for (var i = 0; i < condition.Values.Count; i++)
        {
            condition.Values[i] = NormalizeExtensionFilterValue(condition.Values[i]) ?? "";
        }
    }

    private static string? NormalizeExtensionFilterValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith(".", StringComparison.Ordinal)
            ? trimmed
            : "." + trimmed;
    }

    private static RepositoryIndexQueryResult QueryError(string? from, string message) =>
        new(
            string.IsNullOrWhiteSpace(from) ? "invalid" : from.Trim(),
            ScannedRepositories: 0,
            ScannedRows: 0,
            MatchedRows: 0,
            Truncated: false,
            Offset: 0,
            Limit: DefaultQueryLimit,
            Index: new RepositoryIndexQueryIndexInfo(false, 0, 0, 0, null),
            Warnings:
            [
                message,
                "Call svnhub_index_query_schema for supported fields, aliases, operators, and examples.",
            ],
            Rows: []);

    private static bool MatchesAllConditions(
        Dictionary<string, object?> row,
        IReadOnlyList<RepositoryIndexQueryCondition> conditions) =>
        conditions.All(condition => MatchesCondition(row, condition));

    private static bool MatchesCondition(Dictionary<string, object?> row, RepositoryIndexQueryCondition condition)
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

        if (actual is IEnumerable<string> actualStrings)
        {
            return actualStrings.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                ? 0
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
        (actual is IEnumerable<string> actualStrings
            ? actualStrings.Any(value => value.Contains(expected, StringComparison.OrdinalIgnoreCase))
            : Convert.ToString(actual, CultureInfo.InvariantCulture)?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true);

    private static bool StartsOrEndsWith(object? actual, string? expected, bool starts)
    {
        if (actual is null || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        if (actual is IEnumerable<string> actualStrings)
        {
            return starts
                ? actualStrings.Any(value => value.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
                : actualStrings.Any(value => value.EndsWith(expected, StringComparison.OrdinalIgnoreCase));
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
            CopyLatestFields(latest, grouped, "change.");
            CopyLatestFields(latest, grouped, "tree.");
            CopyLatestFields(latest, grouped, "property.");
            CopyLatestFields(latest, grouped, "external.");
            CopyLatestFields(latest, grouped, "latest.");
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
        IReadOnlyList<RepositoryIndexQueryOrder> orderBy)
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
            select = GetDefaultSelect(from, isGrouped);
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

    private static IReadOnlyList<string> GetDefaultSelect(string from, bool isGrouped)
    {
        if (isGrouped)
        {
            return ["*"];
        }

        return from switch
        {
            "commits" => ["repository.name", "commit.revision", "commit.author", "commit.date", "commit.message", "commit.changedPathCount"],
            "changedpaths" => ["repository.name", "commit.revision", "commit.author", "commit.date", "change.action", "change.path"],
            "tree" => ["repository.name", "tree.path", "tree.name", "tree.extension", "tree.isDirectory", "tree.revision"],
            "properties" => ["repository.name", "property.path", "property.nodeKind", "property.name", "property.value", "property.snapshotRevision"],
            "externals" => ["repository.name", "external.parentPath", "external.targetPath", "external.resolvedPath", "external.url", "external.revision", "external.isPinned"],
            _ => ["repository.name", "repository.labels", "latest.revision", "latest.author", "latest.date", "latest.message", "indexed.remainingRevisions"],
        };
    }

    private static object? GetRowValue(Dictionary<string, object?> row, string field) =>
        row.TryGetValue(NormalizeField(field), out var value) ? value : null;

    private static DateTimeOffset? GetBestDate(Dictionary<string, object?> row)
    {
        if (row.TryGetValue("commit.date", out var commitDate) && commitDate is DateTimeOffset c)
        {
            return c;
        }

        if (row.TryGetValue("latest.date", out var latestDate) && latestDate is DateTimeOffset l)
        {
            return l;
        }

        return null;
    }

    private static long GetBestRevision(Dictionary<string, object?> row)
    {
        if (row.TryGetValue("commit.revision", out var commitRevision) && commitRevision is long c)
        {
            return c;
        }

        if (row.TryGetValue("latest.revision", out var latestRevision) && latestRevision is long l)
        {
            return l;
        }

        if (row.TryGetValue("tree.revision", out var treeRevision) && treeRevision is long t)
        {
            return t;
        }

        if (row.TryGetValue("external.snapshotRevision", out var externalSnapshotRevision) && externalSnapshotRevision is long e)
        {
            return e;
        }

        if (row.TryGetValue("property.snapshotRevision", out var propertySnapshotRevision) && propertySnapshotRevision is long p)
        {
            return p;
        }

        return 0;
    }

    private static void ValidateQuery(string from, RepositoryIndexQueryRequest query)
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
            throw new InvalidOperationException($"Field '{field}' is not allowed for index query source '{DisplaySource(from)}'.");
        }
    }

    private static bool IsAllowedField(string from, string field)
    {
        if (field is "repository.name" or "repository.createdat" or "repository.labels" or
            "repository.rootaccess" or
            "repository.inheritedcontentgrants" or "repository.inheritedmanagementgrants" or
            "indexed.headrevision" or "indexed.revision" or "indexed.headtreerevision" or "indexed.propertiesrevision" or "indexed.externalsrevision" or
            "indexed.remainingrevisions" or "indexed.complete" or
            "indexed.lastsuccessat" or "indexed.lasterror" or "indexed.ismissing")
        {
            return true;
        }

        return from switch
        {
            "repositories" => field is "latest.revision" or "latest.author" or "latest.date" or "latest.message",
            "commits" => field is "commit.revision" or "commit.author" or "commit.date" or "commit.message" or "commit.changedpaths" or "commit.changedpathcount",
            "changedpaths" => field is "commit.revision" or "commit.author" or "commit.date" or "commit.message" or "change.action" or "change.path",
            "tree" => field is "tree.revision" or "tree.path" or "tree.name" or "tree.extension" or "tree.isdirectory",
            "properties" => field is "property.snapshotrevision" or "property.path" or "property.nodekind" or "property.name" or "property.value",
            "externals" => field is "external.snapshotrevision" or "external.parentpath" or "external.targetpath" or
                "external.resolvedpath" or "external.url" or "external.revision" or "external.pegrevision" or "external.ispinned" or "external.raw",
            _ => false,
        };
    }

    private static string NormalizeSource(string? value)
    {
        var normalized = NormalizeQueryToken(value);
        return normalized switch
        {
            "changedpath" or "changedpaths" => "changedpaths",
            "property" or "properties" or "props" => "properties",
            "external" or "externals" or "svnexternals" or "svn:externals" => "externals",
            _ => normalized,
        };
    }

    private static string DisplaySource(string source) =>
        source == "changedpaths" ? "changedPaths" : source;

    private static string NormalizeQueryToken(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeField(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

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

public sealed class RepositoryIndexQueryRequest
{
    public string From { get; set; } = "repositories";
    public RepositoryIndexQueryScan? Scan { get; set; }
    public List<RepositoryIndexQueryCondition> Where { get; set; } = [];
    public List<string> GroupBy { get; set; } = [];
    public List<string> Select { get; set; } = [];
    public List<RepositoryIndexQueryOrder> OrderBy { get; set; } = [];
    public int? Limit { get; set; }
    public int? Offset { get; set; }
}

public sealed class RepositoryIndexQueryScan
{
    public List<string> RepositoryNames { get; set; } = [];
    public string? RepositoryNameContains { get; set; }
}

public sealed class RepositoryIndexQueryCondition
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = "eq";
    public string? Value { get; set; }
    public List<string> Values { get; set; } = [];
}

public sealed class RepositoryIndexQueryOrder
{
    public string Field { get; set; } = "";
    public string Direction { get; set; } = "asc";
}

public sealed record RepositoryIndexQueryResult(
    string From,
    int ScannedRepositories,
    int ScannedRows,
    int MatchedRows,
    bool Truncated,
    int Offset,
    int Limit,
    RepositoryIndexQueryIndexInfo Index,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<Dictionary<string, object?>> Rows);

public sealed record RepositoryIndexQueryIndexInfo(
    bool Complete,
    int MissingRepositories,
    int BehindRepositories,
    long RemainingRevisions,
    DateTimeOffset? LastSuccessAt,
    int StaleSnapshotRepositories = 0);

public sealed record RepositoryIndexQueryChangedPath(string Action, string Path);
