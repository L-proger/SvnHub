using SvnHub.App.Security;
using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class SetupService
{
    private readonly IPortalStore _store;
    private readonly IHtpasswdService _htpasswd;
    private readonly IAuthFilesWriter _authFilesWriter;

    public SetupService(IPortalStore store, IHtpasswdService htpasswd, IAuthFilesWriter authFilesWriter)
    {
        _store = store;
        _htpasswd = htpasswd;
        _authFilesWriter = authFilesWriter;
    }

    public bool IsSetupRequired()
    {
        var state = _store.Read();
        return state.Users.Count == 0;
    }

    public async Task<OperationResult<PortalUser>> CreateInitialAdminAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        if (!Validation.IsValidUserName(userName))
        {
            return OperationResult<PortalUser>.Fail("Invalid user name.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return OperationResult<PortalUser>.Fail("Password must be at least 8 characters.");
        }

        var current = _store.Read();
        if (current.Users.Count != 0)
        {
            return OperationResult<PortalUser>.Fail("Setup is already completed.");
        }

        string bcryptHash;
        try
        {
            bcryptHash = await _htpasswd.CreateBcryptHashAsync(userName, password, cancellationToken);
        }
        catch (Exception ex)
        {
            return OperationResult<PortalUser>.Fail($"Failed to generate SVN password hash via htpasswd: {ex.Message}");
        }

        var admin = new PortalUser(
            Id: Guid.NewGuid(),
            UserName: userName,
            UiPasswordHash: UiPasswordHasher.Hash(password),
            SvnBcryptHash: bcryptHash,
            IsActive: true,
            Roles: PortalUserRoles.AllAdmin,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var newState = current with
        {
            Users = [admin],
            AuditEvents =
            [
                ..current.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: null,
                    Action: "setup.create_admin",
                    Target: admin.UserName,
                    Success: true,
                    Details: null
                ),
            ],
        };

        _store.Write(newState);

        try
        {
            await _authFilesWriter.WriteHtpasswdAsync(newState.Users, cancellationToken);
            await _authFilesWriter.WriteAuthzAsync(newState, cancellationToken);
            await _authFilesWriter.ReloadApacheAsync(cancellationToken);
        }
        catch
        {
            // MVP: setup is complete, but apache sync can be retried later.
        }

        return OperationResult<PortalUser>.Ok(admin);
    }
}
