using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.App.Indexing;

public interface IRepositoryIndexStore
{
    string DatabasePath { get; }

    Task<RepositoryIndexStoreStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryIndexRepositoryHead>> ListRepositoryHeadsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryIndexCommit>> ListCommitsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryIndexChangedPath>> ListChangedPathsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryIndexTreeEntry>> ListHeadTreeEntriesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryIndexProperty>> ListHeadPropertiesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryIndexExternal>> ListHeadExternalsAsync(
        CancellationToken cancellationToken = default);

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

    Task SaveHeadSnapshotAsync(
        Guid repositoryId,
        long revision,
        IReadOnlyList<SvnTreeEntry> treeEntries,
        IReadOnlyList<RepositoryIndexPropertyDefinition> properties,
        IReadOnlyList<RepositoryIndexExternalDefinition> externals,
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
    long HeadTreeRevision,
    long PropertiesRevision,
    long ExternalsRevision,
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
    long HeadTreeEntryCount,
    long HeadPropertyCount,
    long HeadExternalCount,
    DateTimeOffset? LastSuccessAt,
    IReadOnlyList<RepositoryIndexRepositoryState> Repositories);

public sealed record RepositoryIndexRepositoryHead(
    Guid RepositoryId,
    string RepositoryName,
    long YoungestRevision,
    long IndexedRevision,
    long HeadTreeRevision,
    long PropertiesRevision,
    long ExternalsRevision,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    bool IsMissing,
    string? Author,
    DateTimeOffset? Date,
    string? Message);

public sealed record RepositoryIndexCommit(
    Guid RepositoryId,
    string RepositoryName,
    long YoungestRevision,
    long IndexedRevision,
    long Revision,
    string? Author,
    DateTimeOffset Date,
    string? Message,
    IReadOnlyList<SvnChangedPath> ChangedPaths);

public sealed record RepositoryIndexChangedPath(
    Guid RepositoryId,
    string RepositoryName,
    long YoungestRevision,
    long IndexedRevision,
    long Revision,
    string? Author,
    DateTimeOffset Date,
    string? Message,
    string Action,
    string Path);

public sealed record RepositoryIndexTreeEntry(
    Guid RepositoryId,
    string RepositoryName,
    long YoungestRevision,
    long IndexedRevision,
    long HeadTreeRevision,
    string Path,
    string Name,
    string? Extension,
    bool IsDirectory);

public sealed record RepositoryIndexPropertyDefinition(
    string Path,
    string NodeKind,
    string Name,
    string Value);

public sealed record RepositoryIndexProperty(
    Guid RepositoryId,
    string RepositoryName,
    long YoungestRevision,
    long IndexedRevision,
    long PropertiesRevision,
    string Path,
    string NodeKind,
    string Name,
    string Value);

public sealed record RepositoryIndexExternalDefinition(
    string ParentPath,
    string? TargetPath,
    string? ResolvedPath,
    string? Url,
    string? Revision,
    string? PegRevision,
    bool IsPinned,
    string RawDefinition);

public sealed record RepositoryIndexExternal(
    Guid RepositoryId,
    string RepositoryName,
    long YoungestRevision,
    long IndexedRevision,
    long ExternalsRevision,
    string ParentPath,
    string? TargetPath,
    string? ResolvedPath,
    string? Url,
    string? Revision,
    string? PegRevision,
    bool IsPinned,
    string RawDefinition);
