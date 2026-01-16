namespace SvnHub.App.System;

public interface IHtpasswdService
{
    Task<string> CreateBcryptHashAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default
    );
}

