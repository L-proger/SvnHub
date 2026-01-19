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
                var members = ExpandUsers(state, g.Id, usersById, new HashSet<Guid>());

                return (g, members);
            })
            .ToArray();
    }

    public IReadOnlyList<(Group Group, string[] Members, string[] Subgroups)> ListGroupsWithDetails()
    {
        var state = _store.Read();

        var usersById = state.Users
            .Where(u => u.IsActive)
            .ToDictionary(u => u.Id, u => u.UserName);

        var groupsById = state.Groups.ToDictionary(g => g.Id, g => g.Name);

        return state.Groups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var members = ExpandUsers(state, g.Id, usersById, new HashSet<Guid>());

                var subgroups = state.GroupGroupMembers
                    .Where(m => m.GroupId == g.Id)
                    .Select(m => groupsById.GetValueOrDefault(m.ChildGroupId))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return (g, members, subgroups);
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
        catch (Exception ex)
        {
            _store.Write(state);
            return OperationResult<Group>.Fail($"Failed to update authz: {ex.Message}");
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
        catch (Exception ex)
        {
            _store.Write(state);
            return OperationResult.Fail($"Failed to update authz: {ex.Message}");
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> AddSubgroupAsync(
        Guid actorUserId,
        Guid groupId,
        Guid childGroupId,
        CancellationToken cancellationToken = default)
    {
        if (groupId == childGroupId)
        {
            return OperationResult.Fail("A group can't include itself.");
        }

        var state = _store.Read();
        var group = state.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null)
        {
            return OperationResult.Fail("Group not found.");
        }

        var child = state.Groups.FirstOrDefault(g => g.Id == childGroupId);
        if (child is null)
        {
            return OperationResult.Fail("Child group not found.");
        }

        if (state.GroupGroupMembers.Any(m => m.GroupId == groupId && m.ChildGroupId == childGroupId))
        {
            return OperationResult.Fail("This subgroup is already included.");
        }

        // Cycle check: child must not (directly or indirectly) include group.
        if (IsReachable(state, startGroupId: childGroupId, targetGroupId: groupId))
        {
            return OperationResult.Fail("This would create a cycle in group nesting.");
        }

        var newState = state with
        {
            GroupGroupMembers = [..state.GroupGroupMembers, new GroupGroupMember(groupId, childGroupId)],
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "group.add_subgroup",
                    Target: group.Name,
                    Success: true,
                    Details: child.Name
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
            _store.Write(state);
            return OperationResult.Fail($"Failed to update authz: {ex.Message}");
        }

        return OperationResult.Ok();
    }

    private static bool IsReachable(PortalState state, Guid startGroupId, Guid targetGroupId)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(startGroupId);

        while (queue.Count != 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current == targetGroupId)
            {
                return true;
            }

            foreach (var edge in state.GroupGroupMembers)
            {
                if (edge.GroupId == current)
                {
                    queue.Enqueue(edge.ChildGroupId);
                }
            }
        }

        return false;
    }

    private static string[] ExpandUsers(
        PortalState state,
        Guid groupId,
        IReadOnlyDictionary<Guid, string> usersById,
        HashSet<Guid> stack)
    {
        if (!stack.Add(groupId))
        {
            return Array.Empty<string>();
        }

        var members = new List<string>();

        foreach (var m in state.GroupMembers.Where(m => m.GroupId == groupId))
        {
            if (usersById.TryGetValue(m.UserId, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                members.Add(name);
            }
        }

        foreach (var gg in state.GroupGroupMembers.Where(m => m.GroupId == groupId))
        {
            members.AddRange(ExpandUsers(state, gg.ChildGroupId, usersById, stack));
        }

        stack.Remove(groupId);

        return members
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
