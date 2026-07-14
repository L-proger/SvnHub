using SvnHub.App.Indexing;
using SvnHub.App.Storage;
using SvnHub.App.Support;
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

    public async Task<RepositoryDependencyGraphSnapshot> GetDependencyGraphAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var state = _store.Read();
        var access = RepositoryAccessEvaluator.CreateContext(state, userId);
        var visibleRepositories = state.Repositories
            .Where(repository =>
                repository.IsAvailable &&
                access.GetAccess(repository.Id, "/") >= AccessLevel.Read)
            .OrderBy(repository => repository.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(repository => repository.Id)
            .ToDictionary(repository => repository.Id);

        if (visibleRepositories.Count == 0)
        {
            return RepositoryDependencyGraphSnapshot.Empty;
        }

        await _externalTargets.EnsureCurrentAsync(cancellationToken);
        var externals = await _indexStore.ListHeadExternalsAsync(cancellationToken);
        var edgeCounts = new Dictionary<(Guid SourceId, Guid TargetId), DependencyEdgeCounts>();
        var selfReferenceCounts = new Dictionary<Guid, int>();

        foreach (var external in externals)
        {
            if (!visibleRepositories.TryGetValue(external.RepositoryId, out var sourceRepository) ||
                external.TargetRepositoryId is not { } targetRepositoryId ||
                external.TargetRepositoryPath is not { } targetPath ||
                !visibleRepositories.ContainsKey(targetRepositoryId))
            {
                continue;
            }

            var sourceAccessPath = external.ResolvedPath ?? external.ParentPath;
            if (access.GetAccess(sourceRepository.Id, external.ParentPath) < AccessLevel.Read ||
                access.GetAccess(sourceRepository.Id, sourceAccessPath) < AccessLevel.Read ||
                access.GetAccess(targetRepositoryId, targetPath) < AccessLevel.Read)
            {
                continue;
            }

            if (sourceRepository.Id == targetRepositoryId)
            {
                selfReferenceCounts[sourceRepository.Id] =
                    selfReferenceCounts.GetValueOrDefault(sourceRepository.Id) + 1;
                continue;
            }

            var key = (sourceRepository.Id, targetRepositoryId);
            if (!edgeCounts.TryGetValue(key, out var counts))
            {
                counts = new DependencyEdgeCounts();
                edgeCounts.Add(key, counts);
            }

            counts.ReferenceCount++;
            if (external.IsPinned)
            {
                counts.PinnedReferenceCount++;
            }
            else
            {
                counts.UnpinnedReferenceCount++;
            }
        }

        var incomingRepositories = new Dictionary<Guid, int>();
        var outgoingRepositories = new Dictionary<Guid, int>();
        var incomingReferences = new Dictionary<Guid, int>();
        var outgoingReferences = new Dictionary<Guid, int>();
        var edges = edgeCounts
            .Select(pair =>
            {
                var sourceRepository = visibleRepositories[pair.Key.SourceId];
                var targetRepository = visibleRepositories[pair.Key.TargetId];
                incomingRepositories[targetRepository.Id] =
                    incomingRepositories.GetValueOrDefault(targetRepository.Id) + 1;
                outgoingRepositories[sourceRepository.Id] =
                    outgoingRepositories.GetValueOrDefault(sourceRepository.Id) + 1;
                incomingReferences[targetRepository.Id] =
                    incomingReferences.GetValueOrDefault(targetRepository.Id) + pair.Value.ReferenceCount;
                outgoingReferences[sourceRepository.Id] =
                    outgoingReferences.GetValueOrDefault(sourceRepository.Id) + pair.Value.ReferenceCount;

                return new RepositoryDependencyGraphEdge(
                    sourceRepository.Id,
                    targetRepository.Id,
                    pair.Value.ReferenceCount,
                    pair.Value.PinnedReferenceCount,
                    pair.Value.UnpinnedReferenceCount);
            })
            .OrderBy(edge => visibleRepositories[edge.SourceRepositoryId].Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => visibleRepositories[edge.TargetRepositoryId].Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nodes = visibleRepositories.Values
            .Select(repository => new RepositoryDependencyGraphNode(
                repository.Id,
                repository.Name,
                RepositoryLabels.Normalize(repository.Labels),
                incomingRepositories.GetValueOrDefault(repository.Id),
                outgoingRepositories.GetValueOrDefault(repository.Id),
                incomingReferences.GetValueOrDefault(repository.Id),
                outgoingReferences.GetValueOrDefault(repository.Id),
                selfReferenceCounts.GetValueOrDefault(repository.Id)))
            .ToArray();

        return new RepositoryDependencyGraphSnapshot(
            nodes,
            edges,
            edges.Sum(edge => edge.ReferenceCount),
            selfReferenceCounts.Values.Sum());
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

    private sealed class DependencyEdgeCounts
    {
        public int ReferenceCount { get; set; }
        public int PinnedReferenceCount { get; set; }
        public int UnpinnedReferenceCount { get; set; }
    }
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

public sealed record RepositoryDependencyGraphNode(
    Guid RepositoryId,
    string Name,
    IReadOnlyList<string> Labels,
    int IncomingRepositoryCount,
    int OutgoingRepositoryCount,
    int IncomingReferenceCount,
    int OutgoingReferenceCount,
    int SelfReferenceCount);

public sealed record RepositoryDependencyGraphEdge(
    Guid SourceRepositoryId,
    Guid TargetRepositoryId,
    int ReferenceCount,
    int PinnedReferenceCount,
    int UnpinnedReferenceCount);

public sealed record RepositoryDependencyGraphSnapshot(
    IReadOnlyList<RepositoryDependencyGraphNode> Nodes,
    IReadOnlyList<RepositoryDependencyGraphEdge> Edges,
    int InterRepositoryReferenceCount,
    int SelfReferenceCount)
{
    public static RepositoryDependencyGraphSnapshot Empty { get; } = new([], [], 0, 0);
}
