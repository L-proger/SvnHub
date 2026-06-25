using System.Globalization;
using Microsoft.Data.Sqlite;
using SvnHub.App.Configuration;
using SvnHub.App.Indexing;
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
                ReadOptionalDate(reader, 4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt64(6) != 0,
                reader.IsDBNull(7) ? null : reader.GetString(7),
                ReadOptionalDate(reader, 8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
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

        var repositories = new List<RepositoryIndexRepositoryState>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                select repository_id,
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

                create index if not exists ix_revisions_author on revisions(author);
                create index if not exists ix_revisions_date on revisions(date);
                create index if not exists ix_changed_paths_path on changed_paths(path);
                create index if not exists ix_changed_paths_action on changed_paths(action);
                """,
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
            ReadOptionalDate(reader, 5),
            ReadOptionalDate(reader, 6),
            ReadOptionalDate(reader, 7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetInt64(9) != 0,
            ReadRequiredDate(reader, 10));

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
}
