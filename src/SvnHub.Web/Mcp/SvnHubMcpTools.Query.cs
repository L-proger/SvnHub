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
        "This tool never reads live SVN repositories. It searches indexed revisions, changed paths, HEAD tree entries, directory/root properties, and svn:externals declarations, so results can be incomplete when the index is behind HEAD. " +
        "Use from=repositories for one row per visible indexed repository with latest indexed commit and index status fields. " +
        "Use from=commits for indexed revision rows. Use from=changedPaths for indexed changed-path rows. " +
        "Use from=tree for current HEAD file/folder existence by path, name, extension, and isDirectory. " +
        "tree.extension result values include the leading dot, for example .pri, but filters accept either pri or .pri for eq/neq/in. " +
        "Use from=properties for current HEAD SVN properties on repository root and directories only; file properties are not indexed. " +
        "Use from=externals for current HEAD svn:externals declarations; repository.name is the repository declaring the external, while external.targetRepositoryName and external.targetRepositoryPath identify a registered SvnHub target when it can be resolved and read by the user. Exact eq/in filters on targetRepositoryName use the reverse index and are the preferred way to find where a repository is referenced. External targets are not scanned recursively. " +
        "This tool does not read file contents, file sizes, or diffs; use svnhub_read_file, svnhub_list_tree, and svnhub_get_diff for live details. " +
        "Query shape: {from, scan?, where?, select?, groupBy?, orderBy?, limit?, offset?}. " +
        "scan supports repositoryNames and repositoryNameContains. " +
        "Repository labels are available on every source as repository.labels. Aliases label and labels are accepted; eq/in match any label in the repository label list. " +
        "Pagination: default limit is 100, maximum limit is 1000, default offset is 0. If truncated=true, matchedRows is the real total count; repeat the same query with a higher limit or with offset=previous offset+limit until truncated=false. For all rows over 1000, use multiple pages with a stable orderBy. " +
        "Operators: eq, neq, contains, startsWith, endsWith, gt, gte, lt, lte, in, exists. " +
        "Aliases are accepted as standalone fields: repositoryName, label, labels, revision, author, date, message, path, action, name, value, nodeKind, extension, isDirectory, targetPath, resolvedPath, url, targetRepository, targetRepositoryName, targetRepositoryPath, isPinned. Do not combine aliases with slash characters. " +
        "Example repositories where latest indexed commit is by user: {\"from\":\"repositories\",\"where\":[{\"field\":\"author\",\"op\":\"eq\",\"value\":\"USER\"}],\"select\":[\"repositoryName\",\"revision\",\"author\",\"date\",\"message\",\"indexed.remainingRevisions\"]}. " +
        "Example repositories with label PewCB: {\"from\":\"repositories\",\"where\":[{\"field\":\"label\",\"op\":\"eq\",\"value\":\"PewCB\"}],\"select\":[\"repositoryName\",\"labels\",\"revision\",\"author\"]}. " +
        "Example repositories where user participated: {\"from\":\"commits\",\"where\":[{\"field\":\"author\",\"op\":\"eq\",\"value\":\"USER\"}],\"groupBy\":[\"repositoryName\"],\"select\":[\"repositoryName\",\"count\",\"latest.commit.revision\",\"latest.commit.date\",\"latest.commit.message\"]}. " +
        "Example changed paths by user: {\"from\":\"changedPaths\",\"where\":[{\"field\":\"author\",\"op\":\"eq\",\"value\":\"USER\"}],\"select\":[\"repositoryName\",\"revision\",\"date\",\"author\",\"action\",\"path\"]}. " +
        "Example repositories with README.md in HEAD: {\"from\":\"tree\",\"where\":[{\"field\":\"name\",\"op\":\"eq\",\"value\":\"README.md\"}],\"select\":[\"repositoryName\",\"path\",\"isDirectory\"]}. " +
        "Example repositories with .pri files in HEAD: {\"from\":\"tree\",\"where\":[{\"field\":\"extension\",\"op\":\"eq\",\"value\":\"pri\"}],\"groupBy\":[\"repositoryName\"],\"select\":[\"repositoryName\",\"count\"]}. " +
        "Example directory properties: {\"from\":\"properties\",\"where\":[{\"field\":\"name\",\"op\":\"eq\",\"value\":\"svn:externals\"}],\"select\":[\"repositoryName\",\"path\",\"nodeKind\",\"name\",\"value\"]}. " +
        "Example repositories that reference another registered repository: {\"from\":\"externals\",\"where\":[{\"field\":\"targetRepositoryName\",\"op\":\"eq\",\"value\":\"TARGET_REPOSITORY\"}],\"select\":[\"repositoryName\",\"parentPath\",\"resolvedPath\",\"targetRepositoryName\",\"targetRepositoryPath\",\"isPinned\"]}. " +
        "Example unpinned externals: {\"from\":\"externals\",\"where\":[{\"field\":\"isPinned\",\"op\":\"eq\",\"value\":\"false\"}],\"select\":[\"repositoryName\",\"parentPath\",\"targetPath\",\"url\",\"isPinned\",\"raw\"]}. " +
        "Call svnhub_index_query_schema for the full schema and examples.")]
    public static async Task<RepositoryIndexQueryResult> IndexQueryAsync(
        [Description(
            "Index query object. Sources: repositories, commits, changedPaths, tree, properties, externals. " +
            "Use limit and offset for pagination. Default limit is 100, max limit is 1000. If the result has truncated=true, use matchedRows to know the total and request the next page with offset += limit. For datasets over 1000 rows, make multiple calls with the same where/select/orderBy and increasing offset. " +
            "Common fields: repository.name, repository.createdAt, repository.labels, repository.rootAccess, repository.inheritedContentGrants, repository.inheritedManagementGrants, indexed.headRevision, indexed.revision, indexed.headTreeRevision, indexed.propertiesRevision, indexed.externalsRevision, indexed.remainingRevisions, indexed.complete, indexed.lastSuccessAt, indexed.lastError, indexed.isMissing. " +
            "repositories fields: latest.revision, latest.author, latest.date, latest.message. " +
            "commits fields: commit.revision, commit.author, commit.date, commit.message, commit.changedPaths, commit.changedPathCount. " +
            "changedPaths fields: commit.revision, commit.author, commit.date, commit.message, change.action, change.path. " +
            "tree fields: tree.revision, tree.path, tree.name, tree.extension, tree.isDirectory. tree.extension returns values with a leading dot, for example .pri; filters accept pri or .pri. " +
            "properties fields: property.snapshotRevision, property.path, property.nodeKind, property.name, property.value. Properties are indexed only on root and directories. " +
            "externals fields: external.snapshotRevision, external.parentPath, external.targetPath, external.resolvedPath, external.url, external.targetRepositoryName, external.targetRepositoryPath, external.revision, external.pegRevision, external.isPinned, external.raw. repository.name is the declaring repository. Use targetRepositoryName eq/in for an indexed reverse lookup. " +
            "Aliases, each used as its own field string: repositoryName, label, labels, inheritedContentGrants, inheritedManagementGrants, revision, author, date, message, path, action, name, value, nodeKind, extension, isDirectory, targetPath, resolvedPath, url, targetRepository, targetRepositoryName, targetRepositoryPath, isPinned. label/labels match repository.labels. Never send repositoryName/name.")]
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
            IndexOnly: "svnhub_index_query reads only the SvnHub SQLite metadata index. It does not read live SVN, file contents, file sizes, or diffs. Sources tree, properties, and externals use the indexed HEAD snapshot. Properties are indexed only on repository root and directories. Use warnings/index.complete to detect incomplete index coverage.",
            Pagination: "Default limit is 100, maximum limit is 1000, default offset is 0. If truncated=true, matchedRows is the total count. Fetch all data by repeating the same query with the same where/select/groupBy/orderBy and offset increased by limit until truncated=false. Always use a stable orderBy when paging large result sets.",
            Sources:
            [
                new McpIndexQuerySourceSchema(
                    "repositories",
                    "One row per visible indexed repository. Use this for latest indexed commit and index catch-up status.",
                    Fields:
                    [
                        "repository.name",
                        "repository.createdAt",
                        "repository.labels",
                        "repository.rootAccess",
                        "repository.inheritedContentGrants",
                        "repository.inheritedManagementGrants",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.propertiesRevision",
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
                        ["label"] = "repository.labels",
                        ["labels"] = "repository.labels",
                        ["inheritedContentGrants"] = "repository.inheritedContentGrants",
                        ["inheritedManagementGrants"] = "repository.inheritedManagementGrants",
                        ["revision"] = "latest.revision",
                        ["author"] = "latest.author",
                        ["date"] = "latest.date",
                        ["message"] = "latest.message",
                        ["headRevision"] = "indexed.headRevision",
                        ["indexedRevision"] = "indexed.revision",
                        ["headTreeRevision"] = "indexed.headTreeRevision",
                        ["propertiesRevision"] = "indexed.propertiesRevision",
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
                        "repository.labels",
                        "repository.rootAccess",
                        "repository.inheritedContentGrants",
                        "repository.inheritedManagementGrants",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.propertiesRevision",
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
                        ["label"] = "repository.labels",
                        ["labels"] = "repository.labels",
                        ["inheritedContentGrants"] = "repository.inheritedContentGrants",
                        ["inheritedManagementGrants"] = "repository.inheritedManagementGrants",
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
                        "repository.labels",
                        "repository.rootAccess",
                        "repository.inheritedContentGrants",
                        "repository.inheritedManagementGrants",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.propertiesRevision",
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
                        ["label"] = "repository.labels",
                        ["labels"] = "repository.labels",
                        ["inheritedContentGrants"] = "repository.inheritedContentGrants",
                        ["inheritedManagementGrants"] = "repository.inheritedManagementGrants",
                        ["revision"] = "commit.revision",
                        ["author"] = "commit.author",
                        ["date"] = "commit.date",
                        ["message"] = "commit.message",
                        ["action"] = "change.action",
                        ["path"] = "change.path",
                    }),
                new McpIndexQuerySourceSchema(
                    "tree",
                    "One row per visible file or folder in the indexed HEAD snapshot. Use this for current file/folder existence queries. tree.extension result values include the leading dot, for example .pri; eq/neq/in filters also accept pri.",
                    Fields:
                    [
                        "repository.name",
                        "repository.createdAt",
                        "repository.labels",
                        "repository.rootAccess",
                        "repository.inheritedContentGrants",
                        "repository.inheritedManagementGrants",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.propertiesRevision",
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
                        ["label"] = "repository.labels",
                        ["labels"] = "repository.labels",
                        ["inheritedContentGrants"] = "repository.inheritedContentGrants",
                        ["inheritedManagementGrants"] = "repository.inheritedManagementGrants",
                        ["revision"] = "tree.revision",
                        ["snapshotRevision"] = "tree.revision",
                        ["path"] = "tree.path",
                        ["name"] = "tree.name",
                        ["extension"] = "tree.extension",
                        ["isDirectory"] = "tree.isDirectory",
                        ["headTreeRevision"] = "indexed.headTreeRevision",
                        ["propertiesRevision"] = "indexed.propertiesRevision",
                        ["remainingRevisions"] = "indexed.remainingRevisions",
                        ["complete"] = "indexed.complete",
                    }),
                new McpIndexQuerySourceSchema(
                    "externals",
                    "One row per visible svn:externals declaration in the indexed HEAD snapshot. repository.name is the declaring repository. Resolved target repository fields are exposed only when the registered target and target path are readable. Exact eq/in filters on targetRepositoryName use the reverse index. External targets are not recursively scanned.",
                    Fields:
                    [
                        "repository.name",
                        "repository.createdAt",
                        "repository.labels",
                        "repository.rootAccess",
                        "repository.inheritedContentGrants",
                        "repository.inheritedManagementGrants",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.propertiesRevision",
                        "indexed.externalsRevision",
                        "indexed.remainingRevisions",
                        "indexed.complete",
                        "external.snapshotRevision",
                        "external.parentPath",
                        "external.targetPath",
                        "external.resolvedPath",
                        "external.url",
                        "external.targetRepositoryName",
                        "external.targetRepositoryPath",
                        "external.revision",
                        "external.pegRevision",
                        "external.isPinned",
                        "external.raw",
                    ],
                    Aliases: new Dictionary<string, string>
                    {
                        ["repositoryName"] = "repository.name",
                        ["label"] = "repository.labels",
                        ["labels"] = "repository.labels",
                        ["inheritedContentGrants"] = "repository.inheritedContentGrants",
                        ["inheritedManagementGrants"] = "repository.inheritedManagementGrants",
                        ["snapshotRevision"] = "external.snapshotRevision",
                        ["parentPath"] = "external.parentPath",
                        ["targetPath"] = "external.targetPath",
                        ["name"] = "external.targetPath",
                        ["path"] = "external.resolvedPath",
                        ["resolvedPath"] = "external.resolvedPath",
                        ["url"] = "external.url",
                        ["targetRepository"] = "external.targetRepositoryName",
                        ["targetRepositoryName"] = "external.targetRepositoryName",
                        ["targetRepositoryPath"] = "external.targetRepositoryPath",
                        ["revision"] = "external.revision",
                        ["pegRevision"] = "external.pegRevision",
                        ["isPinned"] = "external.isPinned",
                        ["raw"] = "external.raw",
                        ["externalsRevision"] = "indexed.externalsRevision",
                        ["remainingRevisions"] = "indexed.remainingRevisions",
                        ["complete"] = "indexed.complete",
                    }),
                new McpIndexQuerySourceSchema(
                    "properties",
                    "One row per visible SVN property on the repository root or a directory in the indexed HEAD snapshot. File properties are not indexed.",
                    Fields:
                    [
                        "repository.name",
                        "repository.createdAt",
                        "repository.labels",
                        "repository.rootAccess",
                        "repository.inheritedContentGrants",
                        "repository.inheritedManagementGrants",
                        "indexed.headRevision",
                        "indexed.revision",
                        "indexed.headTreeRevision",
                        "indexed.propertiesRevision",
                        "indexed.externalsRevision",
                        "indexed.remainingRevisions",
                        "indexed.complete",
                        "property.snapshotRevision",
                        "property.path",
                        "property.nodeKind",
                        "property.name",
                        "property.value",
                    ],
                    Aliases: new Dictionary<string, string>
                    {
                        ["repositoryName"] = "repository.name",
                        ["label"] = "repository.labels",
                        ["labels"] = "repository.labels",
                        ["inheritedContentGrants"] = "repository.inheritedContentGrants",
                        ["inheritedManagementGrants"] = "repository.inheritedManagementGrants",
                        ["snapshotRevision"] = "property.snapshotRevision",
                        ["path"] = "property.path",
                        ["nodeKind"] = "property.nodeKind",
                        ["name"] = "property.name",
                        ["propertyName"] = "property.name",
                        ["value"] = "property.value",
                        ["propertyValue"] = "property.value",
                        ["propertiesRevision"] = "indexed.propertiesRevision",
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
                    "Repositories with label PewCB",
                    new RepositoryIndexQueryRequest
                    {
                        From = "repositories",
                        Where = [new RepositoryIndexQueryCondition { Field = "label", Op = "eq", Value = "PewCB" }],
                        Select = ["repositoryName", "labels", "revision", "author"],
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
                    "Repositories with .pri files in current HEAD",
                    new RepositoryIndexQueryRequest
                    {
                        From = "tree",
                        Where = [new RepositoryIndexQueryCondition { Field = "extension", Op = "eq", Value = "pri" }],
                        GroupBy = ["repositoryName"],
                        Select = ["repositoryName", "count"],
                    }),
                new McpIndexQueryExample(
                    "Repositories that reference another registered repository",
                    new RepositoryIndexQueryRequest
                    {
                        From = "externals",
                        Where = [new RepositoryIndexQueryCondition { Field = "targetRepositoryName", Op = "eq", Value = "TARGET_REPOSITORY" }],
                        Select = ["repositoryName", "parentPath", "resolvedPath", "targetRepositoryName", "targetRepositoryPath", "revision", "isPinned"],
                    }),
                new McpIndexQueryExample(
                    "Directory/root svn:externals properties in current HEAD",
                    new RepositoryIndexQueryRequest
                    {
                        From = "properties",
                        Where = [new RepositoryIndexQueryCondition { Field = "name", Op = "eq", Value = "svn:externals" }],
                        Select = ["repositoryName", "path", "nodeKind", "name", "value"],
                    }),
                new McpIndexQueryExample(
                    "Unpinned externals in current HEAD",
                    new RepositoryIndexQueryRequest
                    {
                        From = "externals",
                        Where = [new RepositoryIndexQueryCondition { Field = "isPinned", Op = "eq", Value = "false" }],
                        Select = ["repositoryName", "parentPath", "targetPath", "url", "isPinned", "raw"],
                    }),
            ]);
    }
}

public sealed record McpIndexQuerySchemaResult(
    string IndexOnly,
    string Pagination,
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

