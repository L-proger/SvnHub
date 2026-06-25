using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SvnHub.App.Services;

namespace SvnHub.Web.Mcp;

public sealed partial class SvnHubMcpTools
{
    [McpServerTool(
        Name = "svnhub_index_query",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Run a safe declarative query against the SvnHub SQLite metadata index. " +
        "This tool never reads live SVN repositories. It searches indexed revisions, changed paths, HEAD tree entries, and svn:externals declarations, so results can be incomplete when the index is behind HEAD. " +
        "Use from=repositories for one row per visible indexed repository with latest indexed commit and index status fields. " +
        "Use from=commits for indexed revision rows. Use from=changedPaths for indexed changed-path rows. " +
        "Use from=tree for current HEAD file/folder existence by path, name, extension, and isDirectory. " +
        "Use from=externals for current HEAD svn:externals declarations; it indexes declarations only and does not scan external targets recursively. " +
        "This tool does not read file contents, file sizes, or diffs; use svnhub_read_file, svnhub_list_tree, and svnhub_get_diff for live details. " +
        "Query shape: {from, scan?, where?, select?, groupBy?, orderBy?, limit?}. " +
        "scan supports repositoryNames and repositoryNameContains. " +
        "Operators: eq, neq, contains, startsWith, endsWith, gt, gte, lt, lte, in, exists. " +
        "Aliases are accepted as standalone fields: repositoryName, revision, author, date, message, path, action, name, extension, isDirectory, targetPath, resolvedPath, url. Do not combine aliases with slash characters. " +
        "Example repositories where latest indexed commit is by user: {\"from\":\"repositories\",\"where\":[{\"field\":\"author\",\"op\":\"eq\",\"value\":\"USER\"}],\"select\":[\"repositoryName\",\"revision\",\"author\",\"date\",\"message\",\"indexed.remainingRevisions\"]}. " +
        "Example repositories where user participated: {\"from\":\"commits\",\"where\":[{\"field\":\"author\",\"op\":\"eq\",\"value\":\"USER\"}],\"groupBy\":[\"repositoryName\"],\"select\":[\"repositoryName\",\"count\",\"latest.commit.revision\",\"latest.commit.date\",\"latest.commit.message\"]}. " +
        "Example changed paths by user: {\"from\":\"changedPaths\",\"where\":[{\"field\":\"author\",\"op\":\"eq\",\"value\":\"USER\"}],\"select\":[\"repositoryName\",\"revision\",\"date\",\"author\",\"action\",\"path\"]}. " +
        "Example repositories with README.md in HEAD: {\"from\":\"tree\",\"where\":[{\"field\":\"name\",\"op\":\"eq\",\"value\":\"README.md\"}],\"select\":[\"repositoryName\",\"path\",\"isDirectory\"]}. " +
        "Example repositories where Math is declared as an external: {\"from\":\"externals\",\"where\":[{\"field\":\"targetPath\",\"op\":\"contains\",\"value\":\"Math\"}],\"select\":[\"repositoryName\",\"parentPath\",\"targetPath\",\"resolvedPath\",\"url\",\"revision\"]}. " +
        "Call svnhub_index_query_schema for the full schema and examples.")]
    public static async Task<RepositoryIndexQueryResult> IndexQueryAsync(
        [Description(
            "Index query object. Sources: repositories, commits, changedPaths, tree, externals. " +
            "Common fields: repository.name, repository.createdAt, repository.rootAccess, repository.authenticatedDefaultAccess, indexed.headRevision, indexed.revision, indexed.headTreeRevision, indexed.externalsRevision, indexed.remainingRevisions, indexed.complete, indexed.lastSuccessAt, indexed.lastError, indexed.isMissing. " +
            "repositories fields: latest.revision, latest.author, latest.date, latest.message. " +
            "commits fields: commit.revision, commit.author, commit.date, commit.message, commit.changedPaths, commit.changedPathCount. " +
            "changedPaths fields: commit.revision, commit.author, commit.date, commit.message, change.action, change.path. " +
            "tree fields: tree.revision, tree.path, tree.name, tree.extension, tree.isDirectory. " +
            "externals fields: external.snapshotRevision, external.parentPath, external.targetPath, external.resolvedPath, external.url, external.revision, external.pegRevision, external.raw. " +
            "Aliases, each used as its own field string: repositoryName, revision, author, date, message, path, action, name, extension, isDirectory, targetPath, resolvedPath, url. Never send repositoryName/name.")]
        RepositoryIndexQueryRequest query,
        RepositoryIndexQueryService indexQuery,
        IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId(httpContextAccessor);
        return await indexQuery.QueryAsync(query, userId, cancellationToken);
    }

    [McpServerTool(
        Name = "svnhub_index_query_schema",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Return the svnhub_index_query schema, supported fields, aliases, operators, index-only warning, and example queries.")]
    public static McpIndexQuerySchemaResult GetIndexQuerySchema()
    {
        return new McpIndexQuerySchemaResult(
            IndexOnly: "svnhub_index_query reads only the SvnHub SQLite metadata index. It does not read live SVN, file contents, file sizes, or diffs. Sources tree and externals use the indexed HEAD snapshot. Use warnings/index.complete to detect incomplete index coverage.",
            Sources:
            [
                new McpIndexQuerySourceSchema(
                    "repositories",
                    "One row per visible indexed repository. Use this for latest indexed commit and index catch-up status.",
                    Fields:
                    [
                        "repository.name",
                        "repository.createdAt",
                        "repository.rootAccess",
                        "repository.authenticatedDefaultAccess",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.externalsRevision",
                        "indexed.remainingRevisions",
                        "indexed.complete",
                        "indexed.lastSuccessAt",
                        "indexed.lastError",
                        "indexed.isMissing",
                        "latest.revision",
                        "latest.author",
                        "latest.date",
                        "latest.message",
                    ],
                    Aliases: new Dictionary<string, string>
                    {
                        ["name"] = "repository.name",
                        ["repositoryName"] = "repository.name",
                        ["revision"] = "latest.revision",
                        ["author"] = "latest.author",
                        ["date"] = "latest.date",
                        ["message"] = "latest.message",
                        ["headRevision"] = "indexed.headRevision",
                        ["indexedRevision"] = "indexed.revision",
                        ["headTreeRevision"] = "indexed.headTreeRevision",
                        ["externalsRevision"] = "indexed.externalsRevision",
                        ["remainingRevisions"] = "indexed.remainingRevisions",
                        ["complete"] = "indexed.complete",
                    }),
                new McpIndexQuerySourceSchema(
                    "commits",
                    "One row per indexed revision visible to the authenticated user.",
                    Fields:
                    [
                        "repository.name",
                        "repository.createdAt",
                        "repository.rootAccess",
                        "repository.authenticatedDefaultAccess",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.externalsRevision",
                        "indexed.remainingRevisions",
                        "indexed.complete",
                        "commit.revision",
                        "commit.author",
                        "commit.date",
                        "commit.message",
                        "commit.changedPaths",
                        "commit.changedPathCount",
                    ],
                    Aliases: new Dictionary<string, string>
                    {
                        ["name"] = "repository.name",
                        ["repositoryName"] = "repository.name",
                        ["revision"] = "commit.revision",
                        ["author"] = "commit.author",
                        ["date"] = "commit.date",
                        ["message"] = "commit.message",
                        ["changedPaths"] = "commit.changedPaths",
                        ["changedPathCount"] = "commit.changedPathCount",
                    }),
                new McpIndexQuerySourceSchema(
                    "changedPaths",
                    "One row per indexed changed path visible to the authenticated user. Use this to ask what a user changed.",
                    Fields:
                    [
                        "repository.name",
                        "repository.createdAt",
                        "repository.rootAccess",
                        "repository.authenticatedDefaultAccess",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.externalsRevision",
                        "indexed.remainingRevisions",
                        "indexed.complete",
                        "commit.revision",
                        "commit.author",
                        "commit.date",
                        "commit.message",
                        "change.action",
                        "change.path",
                    ],
                    Aliases: new Dictionary<string, string>
                    {
                        ["name"] = "repository.name",
                        ["repositoryName"] = "repository.name",
                        ["revision"] = "commit.revision",
                        ["author"] = "commit.author",
                        ["date"] = "commit.date",
                        ["message"] = "commit.message",
                        ["action"] = "change.action",
                        ["path"] = "change.path",
                    }),
                new McpIndexQuerySourceSchema(
                    "tree",
                    "One row per visible file or folder in the indexed HEAD snapshot. Use this for current file/folder existence queries.",
                    Fields:
                    [
                        "repository.name",
                        "repository.createdAt",
                        "repository.rootAccess",
                        "repository.authenticatedDefaultAccess",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.externalsRevision",
                        "indexed.remainingRevisions",
                        "indexed.complete",
                        "tree.revision",
                        "tree.path",
                        "tree.name",
                        "tree.extension",
                        "tree.isDirectory",
                    ],
                    Aliases: new Dictionary<string, string>
                    {
                        ["repositoryName"] = "repository.name",
                        ["revision"] = "tree.revision",
                        ["snapshotRevision"] = "tree.revision",
                        ["path"] = "tree.path",
                        ["name"] = "tree.name",
                        ["extension"] = "tree.extension",
                        ["isDirectory"] = "tree.isDirectory",
                        ["headTreeRevision"] = "indexed.headTreeRevision",
                        ["remainingRevisions"] = "indexed.remainingRevisions",
                        ["complete"] = "indexed.complete",
                    }),
                new McpIndexQuerySourceSchema(
                    "externals",
                    "One row per visible svn:externals declaration in the indexed HEAD snapshot. External targets are not recursively scanned.",
                    Fields:
                    [
                        "repository.name",
                        "repository.createdAt",
                        "repository.rootAccess",
                        "repository.authenticatedDefaultAccess",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.externalsRevision",
                        "indexed.remainingRevisions",
                        "indexed.complete",
                        "external.snapshotRevision",
                        "external.parentPath",
                        "external.targetPath",
                        "external.resolvedPath",
                        "external.url",
                        "external.revision",
                        "external.pegRevision",
                        "external.raw",
                    ],
                    Aliases: new Dictionary<string, string>
                    {
                        ["repositoryName"] = "repository.name",
                        ["snapshotRevision"] = "external.snapshotRevision",
                        ["parentPath"] = "external.parentPath",
                        ["targetPath"] = "external.targetPath",
                        ["name"] = "external.targetPath",
                        ["path"] = "external.resolvedPath",
                        ["resolvedPath"] = "external.resolvedPath",
                        ["url"] = "external.url",
                        ["revision"] = "external.revision",
                        ["pegRevision"] = "external.pegRevision",
                        ["raw"] = "external.raw",
                        ["externalsRevision"] = "indexed.externalsRevision",
                        ["remainingRevisions"] = "indexed.remainingRevisions",
                        ["complete"] = "indexed.complete",
                    }),
            ],
            Operators: ["eq", "neq", "contains", "startsWith", "endsWith", "gt", "gte", "lt", "lte", "in", "exists"],
            Examples:
            [
                new McpIndexQueryExample(
                    "Repositories where the latest indexed commit is by sergey.serb",
                    new RepositoryIndexQueryRequest
                    {
                        From = "repositories",
                        Where = [new RepositoryIndexQueryCondition { Field = "author", Op = "eq", Value = "sergey.serb" }],
                        Select = ["repositoryName", "revision", "author", "date", "message", "indexed.remainingRevisions"],
                    }),
                new McpIndexQueryExample(
                    "Repositories where sergey.serb has any indexed commits",
                    new RepositoryIndexQueryRequest
                    {
                        From = "commits",
                        Where = [new RepositoryIndexQueryCondition { Field = "author", Op = "eq", Value = "sergey.serb" }],
                        GroupBy = ["repositoryName"],
                        Select = ["repositoryName", "count", "latest.commit.revision", "latest.commit.date", "latest.commit.message"],
                    }),
                new McpIndexQueryExample(
                    "Changed paths committed by sergey.serb",
                    new RepositoryIndexQueryRequest
                    {
                        From = "changedPaths",
                        Where = [new RepositoryIndexQueryCondition { Field = "author", Op = "eq", Value = "sergey.serb" }],
                        Select = ["repositoryName", "revision", "date", "author", "action", "path"],
                        OrderBy = [new RepositoryIndexQueryOrder { Field = "date", Direction = "desc" }],
                    }),
                new McpIndexQueryExample(
                    "Indexed commits touching CMake files",
                    new RepositoryIndexQueryRequest
                    {
                        From = "changedPaths",
                        Where = [new RepositoryIndexQueryCondition { Field = "path", Op = "contains", Value = "CMake" }],
                        Select = ["repositoryName", "revision", "date", "author", "action", "path"],
                    }),
                new McpIndexQueryExample(
                    "Repositories with README.md in current HEAD",
                    new RepositoryIndexQueryRequest
                    {
                        From = "tree",
                        Where = [new RepositoryIndexQueryCondition { Field = "name", Op = "eq", Value = "README.md" }],
                        Select = ["repositoryName", "path", "isDirectory", "revision"],
                    }),
                new McpIndexQueryExample(
                    "Repositories where Math is declared as an svn:externals target",
                    new RepositoryIndexQueryRequest
                    {
                        From = "externals",
                        Where = [new RepositoryIndexQueryCondition { Field = "targetPath", Op = "contains", Value = "Math" }],
                        Select = ["repositoryName", "parentPath", "targetPath", "resolvedPath", "url", "revision"],
                    }),
            ]);
    }
}

public sealed record McpIndexQuerySchemaResult(
    string IndexOnly,
    IReadOnlyList<McpIndexQuerySourceSchema> Sources,
    IReadOnlyList<string> Operators,
    IReadOnlyList<McpIndexQueryExample> Examples);

public sealed record McpIndexQuerySourceSchema(
    string From,
    string Description,
    IReadOnlyList<string> Fields,
    IReadOnlyDictionary<string, string> Aliases);

public sealed record McpIndexQueryExample(
    string Description,
    RepositoryIndexQueryRequest Query);
