using SvnHub.Domain;

namespace SvnHub.App.Indexing;

public interface IRepositoryIndexStore
{
    string DatabasePath { get; }

    Task<RepositoryIndexStoreStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<RepositoryIndexRepositoryState?> GetRepositoryAsync(
        Guid repositoryId,
        CancellationToken cancellationToken = default);

    Task MarkActiveRepositoriesAsync(
        IReadOnlyCollection<Guid> activeRepositoryIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task MarkScanStartedAsync(
        Repository repository,
        long youngestRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task SaveRevisionAsync(
        Guid repositoryId,
        RepositoryIndexedRevision revision,
        IReadOnlyList<SvnChangedPath> changedPaths,
        CancellationToken cancellationToken = default);

    Task MarkScanSucceededAsync(
        Guid repositoryId,
        long indexedRevision,
        long youngestRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task MarkScanFailedAsync(
        Guid repositoryId,
        string error,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public sealed record RepositoryIndexedRevision(
    long Revision,
    DateTimeOffset Date,
    string? Author,
    string? Message);

public sealed record RepositoryIndexRepositoryState(
    Guid RepositoryId,
    string RepositoryName,
    string LocalPath,
    long YoungestRevision,
    long IndexedRevision,
    DateTimeOffset? LastScanStartedAt,
    DateTimeOffset? LastScanCompletedAt,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    bool IsMissing,
    DateTimeOffset UpdatedAt);

public sealed record RepositoryIndexStoreStatus(
    string DatabasePath,
    int RepositoryCount,
    long RevisionCount,
    long ChangedPathCount,
    DateTimeOffset? LastSuccessAt,
    IReadOnlyList<RepositoryIndexRepositoryState> Repositories);
