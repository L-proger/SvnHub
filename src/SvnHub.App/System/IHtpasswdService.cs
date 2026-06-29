namespace SvnHub.App.System;

public interface IHtpasswdService
{
    Task<string> CreateBcryptHashAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default
    );

    Task<bool> VerifyBcryptHashAsync(
        string userName,
        string bcryptHash,
        string password,
        CancellationToken cancellationToken = default
    );
}
