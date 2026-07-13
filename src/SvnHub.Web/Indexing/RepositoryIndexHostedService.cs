using SvnHub.App.Services;

namespace SvnHub.Web.Indexing;

public sealed class RepositoryIndexHostedService : BackgroundService
{
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromSeconds(60);
    private readonly RepositoryIndexService _index;
    private readonly RepositoryExternalTargetIndexService _externalTargets;
    private readonly SettingsService _settings;
    private readonly ILogger<RepositoryIndexHostedService> _logger;

    public RepositoryIndexHostedService(
        RepositoryIndexService index,
        RepositoryExternalTargetIndexService externalTargets,
        SettingsService settings,
        ILogger<RepositoryIndexHostedService> logger)
    {
        _index = index;
        _externalTargets = externalTargets;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _externalTargets.EnsureCurrentAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External target index migration failed.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = _settings.GetEffectiveIndexingSettings();

            if (settings.Enabled)
            {
                try
                {
                    var result = await _index.ScanOnceAsync(force: false, stoppingToken);
                    if (result.Started)
                    {
                        _logger.LogInformation(
                            "Repository index scan completed: {Message}",
                            result.Message);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Repository index scan failed.");
                }
            }

            var delay = settings.Enabled
                ? TimeSpan.FromSeconds(settings.ScanIntervalSeconds)
                : DisabledPollInterval;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
