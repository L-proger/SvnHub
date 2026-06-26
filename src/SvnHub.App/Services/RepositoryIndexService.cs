using SvnHub.App.Indexing;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class RepositoryIndexService
{
    private readonly IRepositoryIndexStore _store;
    private readonly RepositoryService _repositories;
    private readonly SettingsService _settings;
    private readonly ISvnLookClient _svnlook;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _statusGate = new();

    private bool _isRunning;
    private DateTimeOffset? _currentRunStartedAt;
    private string? _currentRepository;
    private int _currentRunTotalRepositories;
    private int _currentRunProcessedRepositories;
    private long _currentRepositoryBaseRevision;
    private long _currentRepositoryCurrentRevision;
    private long _currentRepositoryTargetRevision;
    private DateTimeOffset? _lastRunStartedAt;
    private DateTimeOffset? _lastRunCompletedAt;
    private string? _lastRunSummary;
    private string? _lastRunError;

    public RepositoryIndexService(
        IRepositoryIndexStore store,
        RepositoryService repositories,
        SettingsService settings,
        ISvnLookClient svnlook)
    {
        _store = store;
        _repositories = repositories;
        _settings = settings;
        _svnlook = svnlook;
    }

    public async Task<RepositoryIndexStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var runtime = GetRuntimeStatus();
        var storeStatus = await _store.GetStatusAsync(cancellationToken);

        return new RepositoryIndexStatus(
            _settings.GetEffectiveIndexingSettings(),
            runtime.IsRunning,
            runtime.CurrentRunStartedAt,
            runtime.CurrentRepository,
            runtime.CurrentRunTotalRepositories,
            runtime.CurrentRunProcessedRepositories,
            Math.Max(0, runtime.CurrentRunTotalRepositories - runtime.CurrentRunProcessedRepositories),
            runtime.CurrentRepositoryBaseRevision,
            runtime.CurrentRepositoryCurrentRevision,
            runtime.CurrentRepositoryTargetRevision,
            Math.Max(0, runtime.CurrentRepositoryTargetRevision - runtime.CurrentRepositoryBaseRevision),
            Math.Max(0, runtime.CurrentRepositoryCurrentRevision - runtime.CurrentRepositoryBaseRevision),
            Math.Max(0, runtime.CurrentRepositoryTargetRevision - runtime.CurrentRepositoryCurrentRevision),
            runtime.LastRunStartedAt,
            runtime.LastRunCompletedAt,
            runtime.LastRunSummary,
            runtime.LastRunError,
            storeStatus);
    }

    public async Task<RepositoryIndexRunResult> ScanOnceAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        var indexingSettings = _settings.GetEffectiveIndexingSettings();
        if (!force && !indexingSettings.Enabled)
        {
            return RepositoryIndexRunResult.Skip("Background indexing is disabled.");
        }

        if (!await _scanGate.WaitAsync(0, cancellationToken))
        {
            return RepositoryIndexRunResult.Skip("Indexing is already running.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        SetRunStarted(startedAt);

        return await RunScanWithAcquiredGateAsync(indexingSettings, startedAt, cancellationToken);
    }

    public RepositoryIndexRunResult StartScanInBackground(
        bool force,
        CancellationToken cancellationToken = default)
    {
        var indexingSettings = _settings.GetEffectiveIndexingSettings();
        if (!force && !indexingSettings.Enabled)
        {
            return RepositoryIndexRunResult.Skip("Background indexing is disabled.");
        }

        if (!_scanGate.Wait(0))
        {
            return RepositoryIndexRunResult.Skip("Indexing is already running.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        SetRunStarted(startedAt);

        _ = Task.Run(
            () => RunScanWithAcquiredGateAsync(indexingSettings, startedAt, cancellationToken),
            CancellationToken.None);

        return new RepositoryIndexRunResult(
            true,
            false,
            "Index scan started.",
            0,
            0,
            0,
            0);
    }

    private async Task<RepositoryIndexRunResult> RunScanWithAcquiredGateAsync(
        RepositoryIndexingSettings indexingSettings,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var repositoriesScanned = 0;
        var revisionsIndexed = 0;
        var changedPathsIndexed = 0;
        var failedRepositories = 0;
        var repositoriesWithRemainingRevisions = 0;
        long remainingRevisions = 0;

        try
        {
            var repositories = _repositories.List();
            var processedRepositories = 0;
            SetRunRepositoryProgress(repositories.Count, processedRepositories);

            await _store.MarkActiveRepositoriesAsync(
                repositories.Select(r => r.Id).ToArray(),
                startedAt,
                cancellationToken);

            foreach (var repository in repositories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetRunRepositoryProgress(repositories.Count, processedRepositories);
                SetCurrentRepository(repository.Name);

                try
                {
                    var indexed = await ScanRepositoryAsync(
                        repository,
                        indexingSettings.MaxRevisionsPerRepositoryPerScan,
                        cancellationToken);

                    repositoriesScanned++;
                    revisionsIndexed += indexed.RevisionsIndexed;
                    changedPathsIndexed += indexed.ChangedPathsIndexed;
                    if (indexed.RemainingRevisions > 0)
                    {
                        repositoriesWithRemainingRevisions++;
                        remainingRevisions += indexed.RemainingRevisions;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failedRepositories++;
                    await _store.MarkScanFailedAsync(
                        repository.Id,
                        ShortError(ex),
                        DateTimeOffset.UtcNow,
                        cancellationToken);
                }

                processedRepositories++;
                SetRunRepositoryProgress(repositories.Count, processedRepositories);
                ClearCurrentRepositoryProgress();
            }

            var completedAt = DateTimeOffset.UtcNow;
            var summary = BuildRunSummary(
                repositoriesScanned,
                revisionsIndexed,
                failedRepositories,
                repositoriesWithRemainingRevisions,
                remainingRevisions);

            SetRunCompleted(completedAt, summary, failedRepositories == 0 ? null : summary);

            return new RepositoryIndexRunResult(
                true,
                false,
                summary,
                repositoriesScanned,
                revisionsIndexed,
                changedPathsIndexed,
                failedRepositories);
        }
        catch (OperationCanceledException)
        {
            SetRunCompleted(DateTimeOffset.UtcNow, "Indexing was cancelled.", "Indexing was cancelled.");
            return RepositoryIndexRunResult.Failed("Indexing was cancelled.");
        }
        catch (Exception ex)
        {
            var message = ShortError(ex);
            SetRunCompleted(DateTimeOffset.UtcNow, "Indexing failed.", message);
            return RepositoryIndexRunResult.Failed(message);
        }
        finally
        {
            SetCurrentRepository(null);
            SetRunRepositoryProgress(0, 0);
            ClearCurrentRepositoryProgress();
            _scanGate.Release();
        }
    }

    private async Task<RepositoryIndexRepositoryRunResult> ScanRepositoryAsync(
        Repository repository,
        int maxRevisionsPerRepositoryPerScan,
        CancellationToken cancellationToken)
    {
        var state = await _store.GetRepositoryAsync(repository.Id, cancellationToken);
        await _store.MarkScanStartedAsync(
            repository,
            state?.YoungestRevision ?? state?.IndexedRevision ?? 0,
            DateTimeOffset.UtcNow,
            cancellationToken);

        var youngestRevision = await _svnlook.GetYoungestRevisionAsync(repository.LocalPath, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await _store.MarkScanStartedAsync(repository, youngestRevision, now, cancellationToken);

        state = await _store.GetRepositoryAsync(repository.Id, cancellationToken);
        var indexedRevision = state?.IndexedRevision ?? 0;
        if (youngestRevision <= indexedRevision)
        {
            if (ShouldRefreshHeadSnapshot(state, indexedRevision))
            {
                await SaveHeadSnapshotAsync(repository, indexedRevision, cancellationToken);
            }

            await _store.MarkScanSucceededAsync(
                repository.Id,
                indexedRevision,
                youngestRevision,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return new RepositoryIndexRepositoryRunResult(0, 0);
        }

        var scanToRevision = maxRevisionsPerRepositoryPerScan == 0
            ? youngestRevision
            : Math.Min(youngestRevision, indexedRevision + maxRevisionsPerRepositoryPerScan);

        var revisionsIndexed = 0;
        var changedPathsIndexed = 0;
        SetCurrentRepositoryProgress(repository.Name, indexedRevision, indexedRevision, scanToRevision);

        for (var revisionNumber = indexedRevision + 1; revisionNumber <= scanToRevision; revisionNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var date = await _svnlook.GetRevisionDateAsync(repository.LocalPath, revisionNumber, cancellationToken);
            var author = await _svnlook.GetRevisionAuthorAsync(repository.LocalPath, revisionNumber, cancellationToken);
            var message = await _svnlook.GetRevisionLogAsync(repository.LocalPath, revisionNumber, cancellationToken);
            var changedPaths = await _svnlook.GetChangedPathsAsync(repository.LocalPath, revisionNumber, cancellationToken);

            await _store.SaveRevisionAsync(
                repository.Id,
                new RepositoryIndexedRevision(
                    revisionNumber,
                    date,
                    NullIfWhiteSpace(author),
                    NullIfWhiteSpace(message)),
                changedPaths,
                cancellationToken);

            revisionsIndexed++;
            changedPathsIndexed += changedPaths.Count;
            SetCurrentRepositoryProgress(repository.Name, indexedRevision, revisionNumber, scanToRevision);
        }

        if (scanToRevision == youngestRevision)
        {
            await SaveHeadSnapshotAsync(repository, scanToRevision, cancellationToken);
        }

        await _store.MarkScanSucceededAsync(
            repository.Id,
            scanToRevision,
            youngestRevision,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return new RepositoryIndexRepositoryRunResult(
            revisionsIndexed,
            changedPathsIndexed,
            Math.Max(0, youngestRevision - scanToRevision));
    }

    private async Task SaveHeadSnapshotAsync(
        Repository repository,
        long revision,
        CancellationToken cancellationToken)
    {
        if (revision < 0)
        {
            return;
        }

        var treeEntries = await _svnlook.ListTreeRecursiveAsync(
            repository.LocalPath,
            "/",
            revision,
            cancellationToken);

        var directories = treeEntries
            .Where(e => e.IsDirectory)
            .Select(e => e.Path)
            .Prepend("/")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var properties = new List<RepositoryIndexPropertyDefinition>();
        var externals = new List<RepositoryIndexExternalDefinition>();
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directoryProperties = await _svnlook.GetPropertiesAsync(
                repository.LocalPath,
                directory,
                revision,
                cancellationToken);

            var nodeKind = directory == "/" ? "root" : "directory";
            properties.AddRange(directoryProperties.Select(property =>
                new RepositoryIndexPropertyDefinition(
                    SvnExternalDefinitionParser.NormalizeRepositoryPath(directory),
                    nodeKind,
                    property.Name,
                    property.Value)));

            var externalsProperty = directoryProperties.FirstOrDefault(p =>
                string.Equals(p.Name, "svn:externals", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(externalsProperty?.Value))
            {
                externals.AddRange(ParseExternalDefinitions(directory, externalsProperty.Value));
            }
        }

        await _store.SaveHeadSnapshotAsync(
            repository.Id,
            revision,
            treeEntries,
            properties,
            externals,
            cancellationToken);
    }

    private static bool ShouldRefreshHeadSnapshot(
        RepositoryIndexRepositoryState? state,
        long targetRevision)
    {
        if (targetRevision <= 0 || state is null)
        {
            return false;
        }

        return state.HeadTreeRevision != targetRevision ||
               state.PropertiesRevision != targetRevision ||
               state.ExternalsRevision != targetRevision;
    }

    private static IReadOnlyList<RepositoryIndexExternalDefinition> ParseExternalDefinitions(
        string parentPath,
        string value)
    {
        return SvnExternalDefinitionParser.Parse(parentPath, value)
            .Select(external => new RepositoryIndexExternalDefinition(
                external.ParentPath,
                external.TargetPath,
                external.ResolvedPath,
                external.Url,
                external.Revision,
                external.PegRevision,
                external.IsPinned,
                external.RawDefinition))
            .ToArray();
    }

    private RepositoryIndexRuntimeStatus GetRuntimeStatus()
    {
        lock (_statusGate)
        {
            return new RepositoryIndexRuntimeStatus(
                _isRunning,
                _currentRunStartedAt,
                _currentRepository,
                _currentRunTotalRepositories,
                _currentRunProcessedRepositories,
                _currentRepositoryBaseRevision,
                _currentRepositoryCurrentRevision,
                _currentRepositoryTargetRevision,
                _lastRunStartedAt,
                _lastRunCompletedAt,
                _lastRunSummary,
                _lastRunError);
        }
    }

    private void SetRunStarted(DateTimeOffset startedAt)
    {
        lock (_statusGate)
        {
            _isRunning = true;
            _currentRunStartedAt = startedAt;
            _currentRepository = null;
            _currentRunTotalRepositories = 0;
            _currentRunProcessedRepositories = 0;
            _currentRepositoryBaseRevision = 0;
            _currentRepositoryCurrentRevision = 0;
            _currentRepositoryTargetRevision = 0;
            _lastRunStartedAt = startedAt;
            _lastRunCompletedAt = null;
            _lastRunSummary = "Indexing is running.";
            _lastRunError = null;
        }
    }

    private void SetRunRepositoryProgress(int totalRepositories, int processedRepositories)
    {
        lock (_statusGate)
        {
            _currentRunTotalRepositories = Math.Max(0, totalRepositories);
            _currentRunProcessedRepositories = Math.Clamp(
                processedRepositories,
                0,
                _currentRunTotalRepositories);
        }
    }

    private void SetCurrentRepository(string? repositoryName)
    {
        lock (_statusGate)
        {
            _currentRepository = repositoryName;
        }
    }

    private void SetCurrentRepositoryProgress(
        string repositoryName,
        long baseRevision,
        long currentRevision,
        long targetRevision)
    {
        lock (_statusGate)
        {
            _currentRepository = repositoryName;
            _currentRepositoryBaseRevision = Math.Max(0, baseRevision);
            _currentRepositoryTargetRevision = Math.Max(_currentRepositoryBaseRevision, targetRevision);
            _currentRepositoryCurrentRevision = Math.Clamp(
                currentRevision,
                _currentRepositoryBaseRevision,
                _currentRepositoryTargetRevision);
        }
    }

    private void ClearCurrentRepositoryProgress()
    {
        lock (_statusGate)
        {
            _currentRepositoryBaseRevision = 0;
            _currentRepositoryCurrentRevision = 0;
            _currentRepositoryTargetRevision = 0;
        }
    }

    private void SetRunCompleted(DateTimeOffset completedAt, string summary, string? error)
    {
        lock (_statusGate)
        {
            _isRunning = false;
            _currentRunStartedAt = null;
            _currentRepository = null;
            _currentRunTotalRepositories = 0;
            _currentRunProcessedRepositories = 0;
            _currentRepositoryBaseRevision = 0;
            _currentRepositoryCurrentRevision = 0;
            _currentRepositoryTargetRevision = 0;
            _lastRunCompletedAt = completedAt;
            _lastRunSummary = summary;
            _lastRunError = error;
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ShortError(Exception ex)
    {
        var message = ex.Message.Trim();
        if (message.Length == 0)
        {
            message = ex.GetType().Name;
        }

        return message.Length <= 500 ? message : message[..500];
    }

    private static string BuildRunSummary(
        int repositoriesScanned,
        int revisionsIndexed,
        int failedRepositories,
        int repositoriesWithRemainingRevisions,
        long remainingRevisions)
    {
        var summary = $"Scanned {repositoriesScanned} repositories; indexed {revisionsIndexed} revisions.";
        if (failedRepositories > 0)
        {
            summary += $" {failedRepositories} failed.";
        }

        if (remainingRevisions > 0)
        {
            summary += $" {remainingRevisions} revisions remain in {repositoriesWithRemainingRevisions} repositories due to the per-scan limit.";
        }

        return summary;
    }

    private sealed record RepositoryIndexRepositoryRunResult(
        int RevisionsIndexed,
        int ChangedPathsIndexed,
        long RemainingRevisions = 0);

    private sealed record RepositoryIndexRuntimeStatus(
        bool IsRunning,
        DateTimeOffset? CurrentRunStartedAt,
        string? CurrentRepository,
        int CurrentRunTotalRepositories,
        int CurrentRunProcessedRepositories,
        long CurrentRepositoryBaseRevision,
        long CurrentRepositoryCurrentRevision,
        long CurrentRepositoryTargetRevision,
        DateTimeOffset? LastRunStartedAt,
        DateTimeOffset? LastRunCompletedAt,
        string? LastRunSummary,
        string? LastRunError);
}

public sealed record RepositoryIndexStatus(
    RepositoryIndexingSettings Settings,
    bool IsRunning,
    DateTimeOffset? CurrentRunStartedAt,
    string? CurrentRepository,
    int CurrentRunTotalRepositories,
    int CurrentRunProcessedRepositories,
    int CurrentRunRemainingRepositories,
    long CurrentRepositoryBaseRevision,
    long CurrentRepositoryCurrentRevision,
    long CurrentRepositoryTargetRevision,
    long CurrentRepositoryTotalRevisions,
    long CurrentRepositoryProcessedRevisions,
    long CurrentRepositoryRemainingRevisions,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt,
    string? LastRunSummary,
    string? LastRunError,
    RepositoryIndexStoreStatus Store);

public sealed record RepositoryIndexRunResult(
    bool Started,
    bool Skipped,
    string Message,
    int RepositoriesScanned,
    int RevisionsIndexed,
    int ChangedPathsIndexed,
    int FailedRepositories)
{
    public static RepositoryIndexRunResult Skip(string message) =>
        new(false, true, message, 0, 0, 0, 0);

    public static RepositoryIndexRunResult Failed(string message) =>
        new(false, false, message, 0, 0, 0, 0);
}
