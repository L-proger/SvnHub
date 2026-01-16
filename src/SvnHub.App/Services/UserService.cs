using SvnHub.App.Security;
using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class UserService
{
    private readonly IPortalStore _store;
    private readonly IHtpasswdService _htpasswd;
    private readonly IAuthFilesWriter _authFilesWriter;

    public UserService(IPortalStore store, IHtpasswdService htpasswd, IAuthFilesWriter authFilesWriter)
    {
        _store = store;
        _htpasswd = htpasswd;
        _authFilesWriter = authFilesWriter;
    }

    public OperationResult<PortalUser> Authenticate(string userName, string password)
    {
        var state = _store.Read();
        var user = state.Users.FirstOrDefault(u =>
            string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));

        if (user is null || !user.IsActive)
        {
            return OperationResult<PortalUser>.Fail("Invalid credentials.");
        }

        if (!UiPasswordHasher.Verify(user.UiPasswordHash, password))
        {
            return OperationResult<PortalUser>.Fail("Invalid credentials.");
        }

        return OperationResult<PortalUser>.Ok(user);
    }

    public IReadOnlyList<PortalUser> ListUsers()
    {
        var state = _store.Read();
        return state.Users
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<OperationResult<PortalUser>> CreateUserAsync(
        Guid actorUserId,
        string userName,
        string password,
        PortalRole role,
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

        var state = _store.Read();
        if (state.Users.Any(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<PortalUser>.Fail("User already exists.");
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

        var user = new PortalUser(
            Id: Guid.NewGuid(),
            UserName: userName,
            UiPasswordHash: UiPasswordHasher.Hash(password),
            SvnBcryptHash: bcryptHash,
            IsActive: true,
            Role: role,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var newState = state with
        {
            Users = [..state.Users, user],
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "user.create",
                    Target: user.UserName,
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
        catch (Exception ex)
        {
            return OperationResult<PortalUser>.Fail($"User created, but failed to update Apache auth files: {ex.Message}");
        }

        return OperationResult<PortalUser>.Ok(user);
    }

    public async Task<OperationResult<PortalUser>> ChangePasswordAsync(
        Guid actorUserId,
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            return OperationResult<PortalUser>.Fail("Password must be at least 8 characters.");
        }

        var state = _store.Read();
        var user = state.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult<PortalUser>.Fail("User not found.");
        }

        if (!user.IsActive)
        {
            return OperationResult<PortalUser>.Fail("User is inactive.");
        }

        string bcryptHash;
        try
        {
            bcryptHash = await _htpasswd.CreateBcryptHashAsync(user.UserName, newPassword, cancellationToken);
        }
        catch (Exception ex)
        {
            return OperationResult<PortalUser>.Fail($"Failed to generate SVN password hash via htpasswd: {ex.Message}");
        }

        var updated = user with
        {
            UiPasswordHash = UiPasswordHasher.Hash(newPassword),
            SvnBcryptHash = bcryptHash,
        };

        var newState = state with
        {
            Users = state.Users.Select(u => u.Id == userId ? updated : u).ToList(),
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "user.change_password",
                    Target: user.UserName,
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
        catch (Exception ex)
        {
            return OperationResult<PortalUser>.Fail(
                $"Password changed, but failed to update Apache auth files: {ex.Message}");
        }

        return OperationResult<PortalUser>.Ok(updated);
    }

    public async Task<OperationResult> DeleteUserAsync(
        Guid actorUserId,
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        var state = _store.Read();
        var user = state.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        if (!user.IsActive)
        {
            return OperationResult.Fail("User is already inactive.");
        }

        if (user.Id == actorUserId)
        {
            return OperationResult.Fail("You can't delete yourself.");
        }

        if (user.Role == PortalRole.Admin)
        {
            var activeAdminCount = state.Users.Count(u => u.IsActive && u.Role == PortalRole.Admin);
            if (activeAdminCount <= 1)
            {
                return OperationResult.Fail("You can't delete the last active admin.");
            }
        }

        var updated = user with
        {
            IsActive = false,
            SvnBcryptHash = null,
        };

        var newState = state with
        {
            Users = state.Users.Select(u => u.Id == userId ? updated : u).ToList(),
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "user.delete",
                    Target: user.UserName,
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
        catch (Exception ex)
        {
            return OperationResult.Fail($"User deleted, but failed to update Apache auth files: {ex.Message}");
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult<PortalUser>> ChangeRoleAsync(
        Guid actorUserId,
        Guid userId,
        PortalRole newRole,
        CancellationToken cancellationToken = default
    )
    {
        var state = _store.Read();
        var user = state.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult<PortalUser>.Fail("User not found.");
        }

        if (!user.IsActive)
        {
            return OperationResult<PortalUser>.Fail("User is inactive.");
        }

        if (user.Role == newRole)
        {
            return OperationResult<PortalUser>.Ok(user);
        }

        if (user.Role == PortalRole.Admin && newRole != PortalRole.Admin)
        {
            var activeAdminCount = state.Users.Count(u => u.IsActive && u.Role == PortalRole.Admin);
            if (activeAdminCount <= 1)
            {
                return OperationResult<PortalUser>.Fail("You can't demote the last active admin.");
            }
        }

        var updated = user with { Role = newRole };

        var newState = state with
        {
            Users = state.Users.Select(u => u.Id == userId ? updated : u).ToList(),
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "user.change_role",
                    Target: user.UserName,
                    Success: true,
                    Details: newRole.ToString()
                ),
            ],
        };

        _store.Write(newState);

        try
        {
            await _authFilesWriter.WriteAuthzAsync(newState, cancellationToken);
            await _authFilesWriter.ReloadApacheAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return OperationResult<PortalUser>.Fail(
                $"Role changed, but failed to update Apache auth files: {ex.Message}");
        }

        return OperationResult<PortalUser>.Ok(updated);
    }
}
