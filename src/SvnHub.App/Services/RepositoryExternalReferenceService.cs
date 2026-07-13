using SvnHub.App.Indexing;
using SvnHub.App.Storage;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class RepositoryExternalReferenceService
{
    private readonly IPortalStore _store;
    private readonly IRepositoryIndexStore _indexStore;
    private readonly SettingsService _settings;

    public RepositoryExternalReferenceService(
        IPortalStore store,
        IRepositoryIndexStore indexStore,
        SettingsService settings)
    {
        _store = store;
        _indexStore = indexStore;
        _settings = settings;
    }

    public async Task<RepositoryExternalReferenceSnapshot> ListIncomingAsync(
        Guid userId,
        Guid targetRepositoryId,
        CancellationToken cancellationToken = default)
    {
        var state = _store.Read();
        var repositories = state.Repositories
            .Where(repository => repository.IsAvailable)
            .ToArray();
        var targetRepository = repositories.FirstOrDefault(repository => repository.Id == targetRepositoryId);
        if (targetRepository is null ||
            RepositoryAccessEvaluator.GetAccess(state, userId, targetRepository.Id, "/") < AccessLevel.Read)
        {
            return RepositoryExternalReferenceSnapshot.Empty;
        }

        var visibleSourceRepositories = repositories
            .Where(repository =>
                RepositoryAccessEvaluator.GetAccess(state, userId, repository.Id, "/") >= AccessLevel.Read)
            .ToDictionary(repository => repository.Id);
        var svnBaseUrls = _settings.GetEffectiveSvnBaseUrls(state);
        var externals = await _indexStore.ListHeadExternalsAsync(cancellationToken);
        var rows = new List<RepositoryExternalReference>();

        foreach (var external in externals)
        {
            if (!visibleSourceRepositories.TryGetValue(external.RepositoryId, out var sourceRepository))
            {
                continue;
            }

            var sourceAccessPath = external.ResolvedPath ?? external.ParentPath;
            if (RepositoryAccessEvaluator.GetAccess(state, userId, sourceRepository.Id, external.ParentPath) < AccessLevel.Read ||
                RepositoryAccessEvaluator.GetAccess(state, userId, sourceRepository.Id, sourceAccessPath) < AccessLevel.Read)
            {
                continue;
            }

            var target = SvnExternalTargetResolver.Resolve(
                sourceRepository.Name,
                external.ParentPath,
                repositories,
                svnBaseUrls,
                external.Url);
            if (target is null || target.RepositoryId != targetRepository.Id ||
                RepositoryAccessEvaluator.GetAccess(state, userId, targetRepository.Id, target.Path) < AccessLevel.Read)
            {
                continue;
            }

            var revision = FirstNonEmpty(external.Revision, external.PegRevision);
            rows.Add(new RepositoryExternalReference(
                sourceRepository.Id,
                sourceRepository.Name,
                external.ParentPath,
                external.ResolvedPath ?? external.TargetPath ?? external.ParentPath,
                target.Path,
                ClassifyBranch(external.ParentPath),
                ClassifyBranch(target.Path),
                external.Revision,
                external.PegRevision,
                external.IsPinned,
                long.TryParse(revision, out var numericRevision) ? numericRevision : null,
                external.RawDefinition,
                external.ExternalsRevision));
        }

        var status = await _indexStore.GetStatusAsync(cancellationToken);
        var indexedRepositories = status.Repositories.ToDictionary(repository => repository.RepositoryId);
        var incompleteRepositoryCount = visibleSourceRepositories.Keys.Count(repositoryId =>
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
