using SvnHub.App.Indexing;
using SvnHub.App.System;
using SvnHub.Domain;
using System.Text;

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

        var externals = new List<RepositoryIndexExternalDefinition>();
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = await _svnlook.GetPropertyValueAsync(
                repository.LocalPath,
                directory,
                revision,
                "svn:externals",
                cancellationToken);

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            externals.AddRange(ParseExternalDefinitions(directory, value));
        }

        await _store.SaveHeadSnapshotAsync(
            repository.Id,
            revision,
            treeEntries,
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

        return state.HeadTreeRevision != targetRevision || state.ExternalsRevision != targetRevision;
    }

    private static IReadOnlyList<RepositoryIndexExternalDefinition> ParseExternalDefinitions(
        string parentPath,
        string value)
    {
        var normalizedParent = NormalizeRepositoryPath(parentPath);
        var rows = new List<RepositoryIndexExternalDefinition>();
        foreach (var rawLine in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var rawDefinition = rawLine.Trim();
            if (rawDefinition.Length == 0 || rawDefinition.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add(ParseExternalDefinition(normalizedParent, rawDefinition));
        }

        return rows;
    }

    private static RepositoryIndexExternalDefinition ParseExternalDefinition(
        string parentPath,
        string rawDefinition)
    {
        var tokens = TokenizeExternalDefinition(rawDefinition);
        var revision = ExtractRevision(tokens);
        var urlIndex = tokens.FindIndex(IsExternalUrlToken);

        string? targetPath = null;
        string? url = null;
        string? pegRevision = null;

        if (urlIndex >= 0)
        {
            (url, pegRevision) = SplitPegRevision(tokens[urlIndex]);
            targetPath = urlIndex == tokens.Count - 1
                ? tokens.FirstOrDefault()
                : tokens.ElementAtOrDefault(urlIndex + 1);
        }
        else
        {
            targetPath = tokens.FirstOrDefault();
        }

        targetPath = NullIfWhiteSpace(targetPath);
        url = NullIfWhiteSpace(url);

        return new RepositoryIndexExternalDefinition(
            parentPath,
            targetPath,
            ResolveExternalPath(parentPath, targetPath),
            url,
            NullIfWhiteSpace(revision),
            NullIfWhiteSpace(pegRevision),
            rawDefinition);
    }

    private static List<string> TokenizeExternalDefinition(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        var escaping = false;

        foreach (var ch in value)
        {
            if (escaping)
            {
                current.Append(ch);
                escaping = false;
                continue;
            }

            if (quote is not null)
            {
                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == quote)
                {
                    quote = null;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                FlushToken(tokens, current);
                continue;
            }

            current.Append(ch);
        }

        FlushToken(tokens, current);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static string? ExtractRevision(List<string> tokens)
    {
        string? revision = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.Equals(token, "-r", StringComparison.Ordinal) ||
                string.Equals(token, "--revision", StringComparison.Ordinal))
            {
                if (i + 1 < tokens.Count)
                {
                    revision = tokens[i + 1];
                    tokens.RemoveAt(i + 1);
                }

                tokens.RemoveAt(i);
                i--;
                continue;
            }

            if (token.StartsWith("-r", StringComparison.Ordinal) && token.Length > 2)
            {
                revision = token[2..];
                tokens.RemoveAt(i);
                i--;
                continue;
            }

            const string revisionPrefix = "--revision=";
            if (token.StartsWith(revisionPrefix, StringComparison.Ordinal))
            {
                revision = token[revisionPrefix.Length..];
                tokens.RemoveAt(i);
                i--;
            }
        }

        return revision;
    }

    private static bool IsExternalUrlToken(string token) =>
        token.Contains("://", StringComparison.Ordinal) ||
        token.StartsWith("^/", StringComparison.Ordinal) ||
        token.StartsWith("//", StringComparison.Ordinal) ||
        token.StartsWith("../", StringComparison.Ordinal) ||
        token.StartsWith("./", StringComparison.Ordinal) ||
        token.StartsWith("/", StringComparison.Ordinal);

    private static (string Url, string? PegRevision) SplitPegRevision(string value)
    {
        var at = value.LastIndexOf('@');
        if (at <= 0 || at == value.Length - 1)
        {
            return (value, null);
        }

        var suffix = value[(at + 1)..];
        if (suffix.Contains('/', StringComparison.Ordinal) ||
            suffix.Contains('\\', StringComparison.Ordinal))
        {
            return (value, null);
        }

        return (value[..at], suffix);
    }

    private static string? ResolveExternalPath(string parentPath, string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return null;
        }

        var target = targetPath.Trim().Replace('\\', '/');
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            return NormalizeRepositoryPath(target);
        }

        var parent = NormalizeRepositoryPath(parentPath);
        return parent == "/"
            ? NormalizeRepositoryPath("/" + target)
            : NormalizeRepositoryPath(parent + "/" + target);
    }

    private static string NormalizeRepositoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var p = path.Trim().Replace('\\', '/');
        if (!p.StartsWith("/", StringComparison.Ordinal))
        {
            p = "/" + p;
        }

        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        if (p.Length > 1 && p.EndsWith("/", StringComparison.Ordinal))
        {
            p = p.TrimEnd('/');
        }

        return p;
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
