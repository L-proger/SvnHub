using SvnHub.App.Indexing;
using SvnHub.App.Storage;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class RepositoryExternalReferenceService
{
    private readonly IPortalStore _store;
    private readonly IRepositoryIndexStore _indexStore;
    private readonly RepositoryExternalTargetIndexService _externalTargets;

    public RepositoryExternalReferenceService(
        IPortalStore store,
        IRepositoryIndexStore indexStore,
        RepositoryExternalTargetIndexService externalTargets)
    {
        _store = store;
        _indexStore = indexStore;
        _externalTargets = externalTargets;
    }

    public async Task<RepositoryExternalReferenceSnapshot> ListIncomingAsync(
        Guid userId,
        Guid targetRepositoryId,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadIncomingContextAsync(userId, targetRepositoryId, cancellationToken);
        if (context is null)
        {
            return RepositoryExternalReferenceSnapshot.Empty;
        }

        var rows = new List<RepositoryExternalReference>();
        foreach (var external in context.Externals)
        {
            if (!TryGetVisibleReference(context, external, out var sourceRepository, out var targetPath))
            {
                continue;
            }

            var revision = FirstNonEmpty(external.Revision, external.PegRevision);
            rows.Add(new RepositoryExternalReference(
                sourceRepository.Id,
                sourceRepository.Name,
                external.ParentPath,
                external.ResolvedPath ?? external.TargetPath ?? external.ParentPath,
                targetPath,
                ClassifyBranch(external.ParentPath),
                ClassifyBranch(targetPath),
                external.Revision,
                external.PegRevision,
                external.IsPinned,
                long.TryParse(revision, out var numericRevision) ? numericRevision : null,
                external.RawDefinition,
                external.ExternalsRevision));
        }

        var status = await _indexStore.GetStatusAsync(cancellationToken);
        var indexedRepositories = status.Repositories.ToDictionary(repository => repository.RepositoryId);
        var incompleteRepositoryCount = context.VisibleSourceRepositories.Keys.Count(repositoryId =>
            !indexedRepositories.TryGetValue(repositoryId, out var indexed) ||
            indexed.IsMissing ||
            indexed.IndexedRevision < indexed.YoungestRevision ||
            indexed.ExternalsRevision != indexed.IndexedRevision ||
            !string.IsNullOrWhiteSpace(indexed.LastError));

        return new RepositoryExternalReferenceSnapshot(
            rows
                .OrderBy(row => row.SourceRepositoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.SourceParentPath, StringComparer.Ordinal)
                .ThenBy(row => row.MountedPath, StringComparer.Ordinal)
                .ToArray(),
            incompleteRepositoryCount,
            status.LastSuccessAt);
    }

    public async Task<RepositoryExternalReferenceSummary> GetIncomingSummaryAsync(
        Guid userId,
        Guid targetRepositoryId,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadIncomingContextAsync(userId, targetRepositoryId, cancellationToken);
        if (context is null)
        {
            return RepositoryExternalReferenceSummary.Empty;
        }

        var repositoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referenceCount = 0;
        foreach (var external in context.Externals)
        {
            if (TryGetVisibleReference(context, external, out var sourceRepository, out _))
            {
                repositoryNames.Add(sourceRepository.Name);
                referenceCount++;
            }
        }

        return new RepositoryExternalReferenceSummary(repositoryNames.Count, referenceCount);
    }

    private async Task<IncomingExternalContext?> LoadIncomingContextAsync(
        Guid userId,
        Guid targetRepositoryId,
        CancellationToken cancellationToken)
    {
        var state = _store.Read();
        var access = RepositoryAccessEvaluator.CreateContext(state, userId);
        var repositories = state.Repositories
            .Where(repository => repository.IsAvailable)
            .ToArray();
        var targetRepository = repositories.FirstOrDefault(repository => repository.Id == targetRepositoryId);
        if (targetRepository is null ||
            access.GetAccess(targetRepository.Id, "/") < AccessLevel.Read)
        {
            return null;
        }

        var visibleSourceRepositories = repositories
            .Where(repository =>
                access.GetAccess(repository.Id, "/") >= AccessLevel.Read)
            .ToDictionary(repository => repository.Id);
        await _externalTargets.EnsureCurrentAsync(cancellationToken);
        var externals = await _indexStore.ListHeadExternalsByTargetAsync(
            targetRepository.Id,
            cancellationToken);
        return new IncomingExternalContext(
            access,
            targetRepository,
            visibleSourceRepositories,
            externals);
    }

    private static bool TryGetVisibleReference(
        IncomingExternalContext context,
        RepositoryIndexExternal external,
        out Repository sourceRepository,
        out string targetPath)
    {
        sourceRepository = null!;
        targetPath = "";
        if (!context.VisibleSourceRepositories.TryGetValue(
                external.RepositoryId,
                out var resolvedSourceRepository))
        {
            return false;
        }

        sourceRepository = resolvedSourceRepository;

        var sourceAccessPath = external.ResolvedPath ?? external.ParentPath;
        if (context.Access.GetAccess(sourceRepository.Id, external.ParentPath) < AccessLevel.Read ||
            context.Access.GetAccess(sourceRepository.Id, sourceAccessPath) < AccessLevel.Read ||
            external.TargetRepositoryPath is not { } resolvedTargetPath ||
            context.Access.GetAccess(context.TargetRepository.Id, resolvedTargetPath) < AccessLevel.Read)
        {
            return false;
        }

        targetPath = resolvedTargetPath;
        return true;
    }

    private static string ClassifyBranch(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return "Repository root";
        }

        if (string.Equals(segments[0], "trunk", StringComparison.OrdinalIgnoreCase))
        {
            return "trunk";
        }

        if (segments.Length > 1 &&
            (string.Equals(segments[0], "branches", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "tags", StringComparison.OrdinalIgnoreCase)))
        {
            return $"{segments[0].ToLowerInvariant()}/{segments[1]}";
        }

        return "Repository root";
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;

    private sealed record IncomingExternalContext(
        RepositoryAccessEvaluator.EvaluationContext Access,
        Repository TargetRepository,
        IReadOnlyDictionary<Guid, Repository> VisibleSourceRepositories,
        IReadOnlyList<RepositoryIndexExternal> Externals);
}

public sealed record RepositoryExternalReference(
    Guid SourceRepositoryId,
    string SourceRepositoryName,
    string SourceParentPath,
    string MountedPath,
    string TargetPath,
    string SourceBranch,
    string TargetBranch,
    string? Revision,
    string? PegRevision,
    bool IsPinned,
    long? NumericRevision,
    string RawDefinition,
    long SnapshotRevision);

public sealed record RepositoryExternalReferenceSnapshot(
    IReadOnlyList<RepositoryExternalReference> References,
    int IncompleteRepositoryCount,
    DateTimeOffset? LastIndexSuccessAt)
{
    public static RepositoryExternalReferenceSnapshot Empty { get; } = new([], 0, null);
}

public sealed record RepositoryExternalReferenceSummary(
    int RepositoryCount,
    int ReferenceCount)
{
    public static RepositoryExternalReferenceSummary Empty { get; } = new(0, 0);
}
