using SvnHub.App.Storage;
using SvnHub.App.System;

namespace SvnHub.Web.Support;

public sealed class AuthFilesStartupSyncService : IHostedService
{
    private readonly IPortalStore _store;
    private readonly IAuthFilesWriter _authFilesWriter;
    private readonly ILogger<AuthFilesStartupSyncService> _logger;

    public AuthFilesStartupSyncService(
        IPortalStore store,
        IAuthFilesWriter authFilesWriter,
        ILogger<AuthFilesStartupSyncService> logger)
    {
        _store = store;
        _authFilesWriter = authFilesWriter;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var state = _store.Read();
        await _authFilesWriter.WriteHtpasswdAsync(state.Users, cancellationToken);
        await _authFilesWriter.WriteAuthzAsync(state, cancellationToken);
        _logger.LogInformation("Synchronized generated SVN auth files at startup.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
