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

        try
        {
            var repositories = _repositories.List();
            var processedRepositories = 0;
            SetRepositoryProgress(repositories.Count, processedRepositories, null);

            await _store.MarkActiveRepositoriesAsync(
                repositories.Select(r => r.Id).ToArray(),
                startedAt,
                cancellationToken);

            foreach (var repository in repositories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetRepositoryProgress(repositories.Count, processedRepositories, repository.Name);

                try
                {
                    var indexed = await ScanRepositoryAsync(
                        repository,
                        indexingSettings.MaxRevisionsPerRepositoryPerScan,
                        cancellationToken);

                    repositoriesScanned++;
                    revisionsIndexed += indexed.RevisionsIndexed;
                    changedPathsIndexed += indexed.ChangedPathsIndexed;
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
                SetRepositoryProgress(repositories.Count, processedRepositories, null);
            }

            var completedAt = DateTimeOffset.UtcNow;
            var summary = failedRepositories == 0
                ? $"Indexed {revisionsIndexed} revisions in {repositoriesScanned} repositories."
                : $"Indexed {revisionsIndexed} revisions in {repositoriesScanned} repositories; {failedRepositories} failed.";

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
            SetRepositoryProgress(0, 0, null);
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
            await _store.MarkScanSucceededAsync(
                repository.Id,
                indexedRevision,
                youngestRevision,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return new RepositoryIndexRepositoryRunResult(0, 0);
        }

        var scanToRevision = Math.Min(
            youngestRevision,
            indexedRevision + maxRevisionsPerRepositoryPerScan);

        var revisionsIndexed = 0;
        var changedPathsIndexed = 0;

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
        }

        await _store.MarkScanSucceededAsync(
            repository.Id,
            scanToRevision,
            youngestRevision,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return new RepositoryIndexRepositoryRunResult(revisionsIndexed, changedPathsIndexed);
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
            _lastRunStartedAt = startedAt;
            _lastRunCompletedAt = null;
            _lastRunSummary = "Indexing is running.";
            _lastRunError = null;
        }
    }

    private void SetRepositoryProgress(int totalRepositories, int processedRepositories, string? repositoryName)
    {
        lock (_statusGate)
        {
            _currentRunTotalRepositories = Math.Max(0, totalRepositories);
            _currentRunProcessedRepositories = Math.Clamp(
                processedRepositories,
                0,
                _currentRunTotalRepositories);
            _currentRepository = repositoryName;
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

    private sealed record RepositoryIndexRepositoryRunResult(int RevisionsIndexed, int ChangedPathsIndexed);

    private sealed record RepositoryIndexRuntimeStatus(
        bool IsRunning,
        DateTimeOffset? CurrentRunStartedAt,
        string? CurrentRepository,
        int CurrentRunTotalRepositories,
        int CurrentRunProcessedRepositories,
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
