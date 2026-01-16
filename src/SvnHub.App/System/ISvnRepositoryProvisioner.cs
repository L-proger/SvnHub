namespace SvnHub.App.System;

public interface ISvnRepositoryProvisioner
{
    Task CreateAsync(
        string localPath,
        bool initializeStandardLayout,
        CancellationToken cancellationToken = default
    );
}
