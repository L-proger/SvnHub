using SvnHub.Domain;

namespace SvnHub.App.System;

public interface IAuthFilesWriter
{
    Task WriteHtpasswdAsync(IReadOnlyList<PortalUser> users, CancellationToken cancellationToken = default);
    Task WriteAuthzAsync(PortalState state, CancellationToken cancellationToken = default);
    Task ReloadApacheAsync(CancellationToken cancellationToken = default);
}
