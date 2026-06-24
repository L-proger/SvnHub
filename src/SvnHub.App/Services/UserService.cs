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
        PortalUserRoles roles,
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
        var actor = GetActiveUser(state, actorUserId);
        if (actor is null || !actor.Roles.HasEffectiveRole(PortalUserRoles.AdminUsers))
        {
            return OperationResult<PortalUser>.Fail("You don't have permission to create users.");
        }

        if (roles.HasAnyAdminRole() && !actor.Roles.HasFlag(PortalUserRoles.Owner))
        {
            return OperationResult<PortalUser>.Fail("Only an Owner can assign administrator roles.");
        }

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
            Roles: roles,
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
        var actor = GetActiveUser(state, actorUserId);
        if (actor is null || !actor.Roles.HasEffectiveRole(PortalUserRoles.AdminUsers))
        {
            return OperationResult<PortalUser>.Fail("You don't have permission to change user passwords.");
        }

        var user = state.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult<PortalUser>.Fail("User not found.");
        }

        if (user.Roles.HasFlag(PortalUserRoles.Owner) && !actor.Roles.HasFlag(PortalUserRoles.Owner))
        {
            return OperationResult<PortalUser>.Fail("Only an Owner can change passwords for Owner users.");
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

    public async Task<OperationResult<PortalUser>> ChangeOwnPasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(currentPassword))
        {
            return OperationResult<PortalUser>.Fail("Current password is required.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            return OperationResult<PortalUser>.Fail("Password must be at least 8 characters.");
        }

        var state = _store.Read();
        var user = GetActiveUser(state, userId);
        if (user is null)
        {
            return OperationResult<PortalUser>.Fail("User not found.");
        }

        if (!UiPasswordHasher.Verify(user.UiPasswordHash, currentPassword))
        {
            return OperationResult<PortalUser>.Fail("Current password is incorrect.");
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
                    ActorUserId: userId,
                    Action: "user.change_own_password",
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
        var actor = GetActiveUser(state, actorUserId);
        if (actor is null || !actor.Roles.HasEffectiveRole(PortalUserRoles.AdminUsers))
        {
            return OperationResult.Fail("You don't have permission to delete users.");
        }

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

        if (user.Roles.HasFlag(PortalUserRoles.Owner))
        {
            if (!actor.Roles.HasFlag(PortalUserRoles.Owner))
            {
                return OperationResult.Fail("Only an Owner can delete Owner users.");
            }

            var activeOwnerCount = state.Users.Count(u => u.IsActive && u.Roles.HasFlag(PortalUserRoles.Owner));
            if (activeOwnerCount <= 1)
            {
                return OperationResult.Fail("You can't delete the last active Owner user.");
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

    public async Task<OperationResult<PortalUser>> ChangeRolesAsync(
        Guid actorUserId,
        Guid userId,
        PortalUserRoles newRoles,
        CancellationToken cancellationToken = default
    )
    {
        var state = _store.Read();
        var actor = GetActiveUser(state, actorUserId);
        if (actor is null || !actor.Roles.HasFlag(PortalUserRoles.Owner))
        {
            return OperationResult<PortalUser>.Fail("Only an Owner can change administrator roles.");
        }

        var user = state.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult<PortalUser>.Fail("User not found.");
        }

        if (!user.IsActive)
        {
            return OperationResult<PortalUser>.Fail("User is inactive.");
        }

        if (user.Roles == newRoles)
        {
            return OperationResult<PortalUser>.Ok(user);
        }

        if (user.Roles.HasFlag(PortalUserRoles.Owner) && !newRoles.HasFlag(PortalUserRoles.Owner))
        {
            var activeOwnerCount = state.Users.Count(u => u.IsActive && u.Roles.HasFlag(PortalUserRoles.Owner));
            if (activeOwnerCount <= 1)
            {
                return OperationResult<PortalUser>.Fail("You can't remove Owner from the last active Owner user.");
            }
        }

        var updated = user with { Roles = newRoles };

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
                    Action: "user.change_roles",
                    Target: user.UserName,
                    Success: true,
                    Details: newRoles.ToString()
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

    private static PortalUser? GetActiveUser(PortalState state, Guid userId) =>
        state.Users.FirstOrDefault(u => u.Id == userId && u.IsActive);
}
