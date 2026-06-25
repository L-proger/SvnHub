using System.Globalization;
using Microsoft.Data.Sqlite;
using SvnHub.App.Configuration;
using SvnHub.App.Indexing;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.Infrastructure.Indexing;

public sealed class SqliteRepositoryIndexStore : IRepositoryIndexStore
{
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly string _databasePath;
    private bool _initialized;

    public SqliteRepositoryIndexStore(SvnHubOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _databasePath = ResolveDatabasePath(options);
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string DatabasePath => _databasePath;

    public async Task<IReadOnlyList<RepositoryIndexRepositoryHead>> ListRepositoryHeadsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = new List<RepositoryIndexRepositoryHead>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select ir.repository_id,
                   ir.repository_name,
                   ir.youngest_revision,
                   ir.indexed_revision,
                   ir.head_tree_revision,
                   ir.externals_revision,
                   ir.last_success_at,
                   ir.last_error,
                   ir.is_missing,
                   r.author,
                   r.date,
                   r.message
              from index_repositories ir
              left join revisions r
                on r.repository_id = ir.repository_id
               and r.revision = ir.indexed_revision
             order by lower(ir.repository_name) asc;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RepositoryIndexRepositoryHead(
                ParseGuid(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                ReadOptionalDate(reader, 6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetInt64(8) != 0,
                reader.IsDBNull(9) ? null : reader.GetString(9),
                ReadOptionalDate(reader, 10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<RepositoryIndexCommit>> ListCommitsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var changedPaths = await ReadChangedPathsByRevisionAsync(connection, cancellationToken);
        var rows = new List<RepositoryIndexCommit>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            select ir.repository_id,
                   ir.repository_name,
                   ir.youngest_revision,
                   ir.indexed_revision,
                   r.revision,
                   r.author,
                   r.date,
                   r.message
              from revisions r
              join index_repositories ir on ir.repository_id = r.repository_id
             order by lower(ir.repository_name) asc, r.revision desc;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var repositoryId = ParseGuid(reader.GetString(0));
            var revision = reader.GetInt64(4);
            changedPaths.TryGetValue((repositoryId, revision), out var revisionChangedPaths);

            rows.Add(new RepositoryIndexCommit(
                repositoryId,
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                revision,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                ReadRequiredDate(reader, 6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                revisionChangedPaths ?? []));
        }

        return rows;
    }

    public async Task<IReadOnlyList<RepositoryIndexChangedPath>> ListChangedPathsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = new List<RepositoryIndexChangedPath>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select ir.repository_id,
                   ir.repository_name,
                   ir.youngest_revision,
                   ir.indexed_revision,
                   r.revision,
                   r.author,
                   r.date,
                   r.message,
                   cp.action,
                   cp.path
              from changed_paths cp
              join revisions r
                on r.repository_id = cp.repository_id
               and r.revision = cp.revision
              join index_repositories ir on ir.repository_id = cp.repository_id
             order by lower(ir.repository_name) asc, r.revision desc, cp.ordinal asc;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RepositoryIndexChangedPath(
                ParseGuid(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                ReadRequiredDate(reader, 6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<RepositoryIndexTreeEntry>> ListHeadTreeEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = new List<RepositoryIndexTreeEntry>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select ir.repository_id,
                   ir.repository_name,
                   ir.youngest_revision,
                   ir.indexed_revision,
                   ir.head_tree_revision,
                   hte.path,
                   hte.name,
                   hte.extension,
                   hte.is_directory
              from head_tree_entries hte
              join index_repositories ir on ir.repository_id = hte.repository_id
             order by lower(ir.repository_name) asc, hte.path asc;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RepositoryIndexTreeEntry(
                ParseGuid(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetInt64(8) != 0));
        }

        return rows;
    }

    public async Task<IReadOnlyList<RepositoryIndexProperty>> ListHeadPropertiesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = new List<RepositoryIndexProperty>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select ir.repository_id,
                   ir.repository_name,
                   ir.youngest_revision,
                   ir.indexed_revision,
                   ir.externals_revision,
                   hp.path,
                   hp.node_kind,
                   hp.property_name,
                   hp.property_value
              from head_properties hp
              join index_repositories ir on ir.repository_id = hp.repository_id
             order by lower(ir.repository_name) asc, hp.path asc, hp.property_name asc;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RepositoryIndexProperty(
                ParseGuid(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<RepositoryIndexExternal>> ListHeadExternalsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = new List<RepositoryIndexExternal>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select ir.repository_id,
                   ir.repository_name,
                   ir.youngest_revision,
                   ir.indexed_revision,
                   ir.externals_revision,
                   he.parent_path,
                   he.target_path,
                   he.resolved_path,
                   he.url,
                   he.revision,
                   he.peg_revision,
                   he.is_pinned,
                   he.raw_definition
              from head_externals he
              join index_repositories ir on ir.repository_id = he.repository_id
             order by lower(ir.repository_name) asc, he.parent_path asc, he.ordinal asc;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RepositoryIndexExternal(
                ParseGuid(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetInt64(11) != 0,
                reader.GetString(12)));
        }

        return rows;
    }

    public async Task<RepositoryIndexStoreStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var repositoryCount = (int)await ExecuteScalarLongAsync(
            connection,
            "select count(*) from index_repositories;",
            cancellationToken);
        var revisionCount = await ExecuteScalarLongAsync(
            connection,
            "select count(*) from revisions;",
            cancellationToken);
        var changedPathCount = await ExecuteScalarLongAsync(
            connection,
            "select count(*) from changed_paths;",
            cancellationToken);
        var headTreeEntryCount = await ExecuteScalarLongAsync(
            connection,
            "select count(*) from head_tree_entries;",
            cancellationToken);
        var headPropertyCount = await ExecuteScalarLongAsync(
            connection,
            "select count(*) from head_properties;",
            cancellationToken);
        var headExternalCount = await ExecuteScalarLongAsync(
            connection,
            "select count(*) from head_externals;",
            cancellationToken);

        var repositories = new List<RepositoryIndexRepositoryState>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                select repository_id,
                       repository_name,
                       local_path,
                       youngest_revision,
                       indexed_revision,
                       head_tree_revision,
                       externals_revision,
                       last_scan_started_at,
                       last_scan_completed_at,
                       last_success_at,
                       last_error,
                       is_missing,
                       updated_at
                  from index_repositories
                 order by is_missing asc, lower(repository_name) asc;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                repositories.Add(ReadRepositoryState(reader));
            }
        }

        return new RepositoryIndexStoreStatus(
            _databasePath,
            repositoryCount,
            revisionCount,
            changedPathCount,
            headTreeEntryCount,
            headPropertyCount,
            headExternalCount,
            repositories
                .Where(r => r.LastSuccessAt is not null)
                .Select(r => r.LastSuccessAt)
                .DefaultIfEmpty()
                .Max(),
            repositories);
    }

    public async Task<RepositoryIndexRepositoryState?> GetRepositoryAsync(
        Guid repositoryId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            select repository_id,
                   repository_name,
                   local_path,
                   youngest_revision,
                   indexed_revision,
                   head_tree_revision,
                   externals_revision,
                   last_scan_started_at,
                   last_scan_completed_at,
                   last_success_at,
                   last_error,
                   is_missing,
                   updated_at
              from index_repositories
             where repository_id = $repositoryId;
            """;
        command.Parameters.AddWithValue("$repositoryId", FormatGuid(repositoryId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRepositoryState(reader) : null;
    }

    public async Task MarkActiveRepositoriesAsync(
        IReadOnlyCollection<Guid> activeRepositoryIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "update index_repositories set is_missing = 1, updated_at = $now;",
            [("$now", FormatDate(now))],
            cancellationToken);

        foreach (var repositoryId in activeRepositoryIds)
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                "update index_repositories set is_missing = 0, updated_at = $now where repository_id = $repositoryId;",
                [
                    ("$now", FormatDate(now)),
                    ("$repositoryId", FormatGuid(repositoryId)),
                ],
                cancellationToken);
        }

        transaction.Commit();
    }

    public async Task MarkScanStartedAsync(
        Repository repository,
        long youngestRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            null,
            """
            insert into index_repositories (
                repository_id,
                repository_name,
                local_path,
                youngest_revision,
                indexed_revision,
                last_scan_started_at,
                last_scan_completed_at,
                last_success_at,
                last_error,
                is_missing,
                updated_at
            ) values (
                $repositoryId,
                $repositoryName,
                $localPath,
                $youngestRevision,
                0,
                $now,
                null,
                null,
                null,
                0,
                $now
            )
            on conflict(repository_id) do update set
                repository_name = excluded.repository_name,
                local_path = excluded.local_path,
                youngest_revision = excluded.youngest_revision,
                last_scan_started_at = excluded.last_scan_started_at,
                last_error = null,
                is_missing = 0,
                updated_at = excluded.updated_at;
            """,
            [
                ("$repositoryId", FormatGuid(repository.Id)),
                ("$repositoryName", repository.Name),
                ("$localPath", repository.LocalPath),
                ("$youngestRevision", youngestRevision),
                ("$now", FormatDate(now)),
            ],
            cancellationToken);
    }

    public async Task SaveRevisionAsync(
        Guid repositoryId,
        RepositoryIndexedRevision revision,
        IReadOnlyList<SvnChangedPath> changedPaths,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            insert into revisions (
                repository_id,
                revision,
                author,
                date,
                message
            ) values (
                $repositoryId,
                $revision,
                $author,
                $date,
                $message
            )
            on conflict(repository_id, revision) do update set
                author = excluded.author,
                date = excluded.date,
                message = excluded.message;
            """,
            [
                ("$repositoryId", FormatGuid(repositoryId)),
                ("$revision", revision.Revision),
                ("$author", DbValue(revision.Author)),
                ("$date", FormatDate(revision.Date)),
                ("$message", DbValue(revision.Message)),
            ],
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "delete from changed_paths where repository_id = $repositoryId and revision = $revision;",
            [
                ("$repositoryId", FormatGuid(repositoryId)),
                ("$revision", revision.Revision),
            ],
            cancellationToken);

        for (var i = 0; i < changedPaths.Count; i++)
        {
            var changedPath = changedPaths[i];
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                insert into changed_paths (
                    repository_id,
                    revision,
                    ordinal,
                    action,
                    path
                ) values (
                    $repositoryId,
                    $revision,
                    $ordinal,
                    $action,
                    $path
                );
                """,
                [
                    ("$repositoryId", FormatGuid(repositoryId)),
                    ("$revision", revision.Revision),
                    ("$ordinal", i),
                    ("$action", changedPath.Action),
                    ("$path", changedPath.Path),
                ],
                cancellationToken);
        }

        transaction.Commit();
    }

    public async Task SaveHeadSnapshotAsync(
        Guid repositoryId,
        long revision,
        IReadOnlyList<SvnTreeEntry> treeEntries,
        IReadOnlyList<RepositoryIndexPropertyDefinition> properties,
        IReadOnlyList<RepositoryIndexExternalDefinition> externals,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "delete from head_tree_entries where repository_id = $repositoryId;",
            [("$repositoryId", FormatGuid(repositoryId))],
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "delete from head_properties where repository_id = $repositoryId;",
            [("$repositoryId", FormatGuid(repositoryId))],
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "delete from head_externals where repository_id = $repositoryId;",
            [("$repositoryId", FormatGuid(repositoryId))],
            cancellationToken);

        foreach (var entry in treeEntries)
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                insert into head_tree_entries (
                    repository_id,
                    path,
                    name,
                    extension,
                    is_directory,
                    revision
                ) values (
                    $repositoryId,
                    $path,
                    $name,
                    $extension,
                    $isDirectory,
                    $revision
                );
                """,
                [
                    ("$repositoryId", FormatGuid(repositoryId)),
                    ("$path", entry.Path),
                    ("$name", entry.Name),
                    ("$extension", DbValue(GetExtension(entry))),
                    ("$isDirectory", entry.IsDirectory ? 1 : 0),
                    ("$revision", revision),
                ],
                cancellationToken);
        }

        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                insert into head_properties (
                    repository_id,
                    path,
                    property_name,
                    property_value,
                    node_kind,
                    snapshot_revision
                ) values (
                    $repositoryId,
                    $path,
                    $propertyName,
                    $propertyValue,
                    $nodeKind,
                    $snapshotRevision
                );
                """,
                [
                    ("$repositoryId", FormatGuid(repositoryId)),
                    ("$path", property.Path),
                    ("$propertyName", property.Name),
                    ("$propertyValue", property.Value),
                    ("$nodeKind", property.NodeKind),
                    ("$snapshotRevision", revision),
                ],
                cancellationToken);
        }

        for (var i = 0; i < externals.Count; i++)
        {
            var external = externals[i];
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                insert into head_externals (
                    repository_id,
                    ordinal,
                    parent_path,
                    target_path,
                    resolved_path,
                    url,
                    revision,
                    peg_revision,
                    is_pinned,
                    raw_definition,
                    snapshot_revision
                ) values (
                    $repositoryId,
                    $ordinal,
                    $parentPath,
                    $targetPath,
                    $resolvedPath,
                    $url,
                    $revision,
                    $pegRevision,
                    $isPinned,
                    $rawDefinition,
                    $snapshotRevision
                );
                """,
                [
                    ("$repositoryId", FormatGuid(repositoryId)),
                    ("$ordinal", i),
                    ("$parentPath", external.ParentPath),
                    ("$targetPath", DbValue(external.TargetPath)),
                    ("$resolvedPath", DbValue(external.ResolvedPath)),
                    ("$url", DbValue(external.Url)),
                    ("$revision", DbValue(external.Revision)),
                    ("$pegRevision", DbValue(external.PegRevision)),
                    ("$isPinned", external.IsPinned ? 1 : 0),
                    ("$rawDefinition", external.RawDefinition),
                    ("$snapshotRevision", revision),
                ],
                cancellationToken);
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            update index_repositories
               set head_tree_revision = $revision,
                   externals_revision = $revision,
                   updated_at = $now
             where repository_id = $repositoryId;
            """,
            [
                ("$revision", revision),
                ("$now", FormatDate(DateTimeOffset.UtcNow)),
                ("$repositoryId", FormatGuid(repositoryId)),
            ],
            cancellationToken);

        transaction.Commit();
    }

    public async Task MarkScanSucceededAsync(
        Guid repositoryId,
        long indexedRevision,
        long youngestRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            null,
            """
            update index_repositories
               set indexed_revision = $indexedRevision,
                   youngest_revision = $youngestRevision,
                   last_scan_completed_at = $now,
                   last_success_at = $now,
                   last_error = null,
                   is_missing = 0,
                   updated_at = $now
             where repository_id = $repositoryId;
            """,
            [
                ("$indexedRevision", indexedRevision),
                ("$youngestRevision", youngestRevision),
                ("$now", FormatDate(now)),
                ("$repositoryId", FormatGuid(repositoryId)),
            ],
            cancellationToken);
    }

    public async Task MarkScanFailedAsync(
        Guid repositoryId,
        string error,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            null,
            """
            update index_repositories
               set last_scan_completed_at = $now,
                   last_error = $error,
                   updated_at = $now
             where repository_id = $repositoryId;
            """,
            [
                ("$now", FormatDate(now)),
                ("$error", error),
                ("$repositoryId", FormatGuid(repositoryId)),
            ],
            cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await ExecuteNonQueryAsync(connection, null, "pragma journal_mode = wal;", [], cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "pragma foreign_keys = on;", [], cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                null,
                """
                create table if not exists index_repositories (
                    repository_id text primary key,
                    repository_name text not null,
                    local_path text not null,
                    youngest_revision integer not null default 0,
                    indexed_revision integer not null default 0,
                    head_tree_revision integer not null default 0,
                    externals_revision integer not null default 0,
                    last_scan_started_at text null,
                    last_scan_completed_at text null,
                    last_success_at text null,
                    last_error text null,
                    is_missing integer not null default 0,
                    updated_at text not null
                );

                create table if not exists revisions (
                    repository_id text not null,
                    revision integer not null,
                    author text null,
                    date text not null,
                    message text null,
                    primary key (repository_id, revision),
                    foreign key (repository_id) references index_repositories(repository_id) on delete cascade
                );

                create table if not exists changed_paths (
                    repository_id text not null,
                    revision integer not null,
                    ordinal integer not null,
                    action text not null,
                    path text not null,
                    primary key (repository_id, revision, ordinal),
                    foreign key (repository_id, revision) references revisions(repository_id, revision) on delete cascade
                );

                create table if not exists head_tree_entries (
                    repository_id text not null,
                    path text not null,
                    name text not null,
                    extension text null,
                    is_directory integer not null,
                    revision integer not null,
                    primary key (repository_id, path),
                    foreign key (repository_id) references index_repositories(repository_id) on delete cascade
                );

                create table if not exists head_properties (
                    repository_id text not null,
                    path text not null,
                    property_name text not null,
                    property_value text not null,
                    node_kind text not null,
                    snapshot_revision integer not null,
                    primary key (repository_id, path, property_name),
                    foreign key (repository_id) references index_repositories(repository_id) on delete cascade
                );

                create table if not exists head_externals (
                    repository_id text not null,
                    ordinal integer not null,
                    parent_path text not null,
                    target_path text null,
                    resolved_path text null,
                    url text null,
                    revision text null,
                    peg_revision text null,
                    is_pinned integer not null default 0,
                    raw_definition text not null,
                    snapshot_revision integer not null,
                    primary key (repository_id, ordinal),
                    foreign key (repository_id) references index_repositories(repository_id) on delete cascade
                );

                create index if not exists ix_revisions_author on revisions(author);
                create index if not exists ix_revisions_date on revisions(date);
                create index if not exists ix_changed_paths_path on changed_paths(path);
                create index if not exists ix_changed_paths_action on changed_paths(action);
                create index if not exists ix_head_tree_entries_name on head_tree_entries(name);
                create index if not exists ix_head_tree_entries_path on head_tree_entries(path);
                create index if not exists ix_head_tree_entries_extension on head_tree_entries(extension);
                create index if not exists ix_head_properties_name on head_properties(property_name);
                create index if not exists ix_head_properties_path on head_properties(path);
                create index if not exists ix_head_properties_node_kind on head_properties(node_kind);
                create index if not exists ix_head_externals_target_path on head_externals(target_path);
                create index if not exists ix_head_externals_resolved_path on head_externals(resolved_path);
                create index if not exists ix_head_externals_url on head_externals(url);
                """,
                [],
                cancellationToken);

            await EnsureColumnAsync(
                connection,
                "index_repositories",
                "head_tree_revision",
                "head_tree_revision integer not null default 0",
                cancellationToken);

            await EnsureColumnAsync(
                connection,
                "index_repositories",
                "externals_revision",
                "externals_revision integer not null default 0",
                cancellationToken);

            await EnsureColumnAsync(
                connection,
                "head_externals",
                "is_pinned",
                "is_pinned integer not null default 0",
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                null,
                "create index if not exists ix_head_externals_is_pinned on head_externals(is_pinned);",
                [],
                cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        return new SqliteConnection(builder.ToString());
    }

    private static async Task<Dictionary<(Guid RepositoryId, long Revision), List<SvnChangedPath>>> ReadChangedPathsByRevisionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(Guid RepositoryId, long Revision), List<SvnChangedPath>>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select repository_id, revision, action, path
              from changed_paths
             order by repository_id asc, revision desc, ordinal asc;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (ParseGuid(reader.GetString(0)), reader.GetInt64(1));
            if (!result.TryGetValue(key, out var rows))
            {
                rows = [];
                result[key] = rows;
            }

            rows.Add(new SvnChangedPath(reader.GetString(2), reader.GetString(3)));
        }

        return result;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        var hasColumn = false;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"pragma table_info({tableName});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (hasColumn)
        {
            return;
        }

        await ExecuteNonQueryAsync(
            connection,
            null,
            $"alter table {tableName} add column {columnDefinition};",
            [],
            cancellationToken);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null || value is DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static RepositoryIndexRepositoryState ReadRepositoryState(SqliteDataReader reader) =>
        new(
            ParseGuid(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            ReadOptionalDate(reader, 7),
            ReadOptionalDate(reader, 8),
            ReadOptionalDate(reader, 9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetInt64(11) != 0,
            ReadRequiredDate(reader, 12));

    private static string ResolveDatabasePath(SvnHubOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.IndexDatabasePath))
        {
            return Path.Combine(options.DataDirectory, "index", "svnhub-index.db");
        }

        var trimmed = options.IndexDatabasePath.Trim();
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(options.DataDirectory, trimmed));
    }

    private static string FormatGuid(Guid value) => value.ToString("D");

    private static Guid ParseGuid(string value) => Guid.Parse(value);

    private static string FormatDate(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ReadRequiredDate(SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? ReadOptionalDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadRequiredDate(reader, ordinal);

    private static object DbValue(string? value) =>
        string.IsNullOrEmpty(value) ? DBNull.Value : value;

    private static string? GetExtension(SvnTreeEntry entry)
    {
        if (entry.IsDirectory)
        {
            return null;
        }

        var extension = Path.GetExtension(entry.Name);
        return string.IsNullOrWhiteSpace(extension) ? null : extension.ToLowerInvariant();
    }
}
