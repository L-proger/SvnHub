namespace SvnHub.App.System;

public interface ISvnRepositoryProvisioner
{
    Task CreateAsync(
        string localPath,
        bool initializeStandardLayout,
        string? authorUserName = null,
        CancellationToken cancellationToken = default
    );
}
