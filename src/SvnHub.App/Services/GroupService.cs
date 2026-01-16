using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class GroupService
{
    private readonly IPortalStore _store;
    private readonly IAuthFilesWriter _authFilesWriter;

    public GroupService(IPortalStore store, IAuthFilesWriter authFilesWriter)
    {
        _store = store;
        _authFilesWriter = authFilesWriter;
    }

    public IReadOnlyList<Group> ListGroups()
    {
        var state = _store.Read();
        return state.Groups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<(Group Group, string[] Members)> ListGroupsWithMembers()
    {
        var state = _store.Read();

        var usersById = state.Users
            .Where(u => u.IsActive)
            .ToDictionary(u => u.Id, u => u.UserName);

        return state.Groups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var members = state.GroupMembers
                    .Where(m => m.GroupId == g.Id)
                    .Select(m => usersById.GetValueOrDefault(m.UserId))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return (g, members);
            })
            .ToArray();
    }

    public async Task<OperationResult<Group>> CreateGroupAsync(
        Guid actorUserId,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        if (!Validation.IsValidGroupName(name))
        {
            return OperationResult<Group>.Fail("Invalid group name.");
        }

        var state = _store.Read();
        if (state.Groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<Group>.Fail("Group already exists.");
        }

        var group = new Group(Guid.NewGuid(), name, DateTimeOffset.UtcNow);

        var newState = state with
        {
            Groups = [..state.Groups, group],
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "group.create",
                    Target: group.Name,
                    Success: true,
                    Details: null
                ),
            ],
        };

        _store.Write(newState);

        try
        {
            await _authFilesWriter.WriteAuthzAsync(newState, cancellationToken);
            await _authFilesWriter.ReloadApacheAsync(cancellationToken);
        }
        catch
        {
            // MVP: group is created, authz sync can be retried later.
        }

        return OperationResult<Group>.Ok(group);
    }

    public async Task<OperationResult> AddMemberAsync(
        Guid actorUserId,
        Guid groupId,
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        var state = _store.Read();
        var group = state.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null)
        {
            return OperationResult.Fail("Group not found.");
        }

        var user = state.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        if (!user.IsActive)
        {
            return OperationResult.Fail("User is inactive.");
        }

        if (state.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == userId))
        {
            return OperationResult.Fail("User is already a member of this group.");
        }

        var newState = state with
        {
            GroupMembers = [..state.GroupMembers, new GroupMember(groupId, userId)],
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "group.add_member",
                    Target: group.Name,
                    Success: true,
                    Details: user.UserName
                ),
            ],
        };

        _store.Write(newState);

        try
        {
            await _authFilesWriter.WriteAuthzAsync(newState, cancellationToken);
            await _authFilesWriter.ReloadApacheAsync(cancellationToken);
        }
        catch
        {
        }

        return OperationResult.Ok();
    }
}

