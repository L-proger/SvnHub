using SvnHub.App.Storage;
using SvnHub.App.Support;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class PermissionService
{
    private readonly IPortalStore _store;
    private readonly IAuthFilesWriter _authFilesWriter;

    public PermissionService(IPortalStore store, IAuthFilesWriter authFilesWriter)
    {
        _store = store;
        _authFilesWriter = authFilesWriter;
    }

    public IReadOnlyList<PermissionRule> ListRules()
    {
        var state = _store.Read();
        return state.PermissionRules
            .OrderBy(r => r.RepositoryId)
            .ThenBy(r => r.Path, StringComparer.Ordinal)
            .ThenBy(r => r.SubjectType)
            .ThenBy(r => r.SubjectId)
            .ToArray();
    }

    public async Task<OperationResult<PermissionRule>> AddRuleAsync(
        Guid actorUserId,
        Guid repositoryId,
        string path,
        SubjectType subjectType,
        Guid subjectId,
        AccessLevel access,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedPath = NormalizeAuthzPath(path);
        if (normalizedPath is null)
        {
            return OperationResult<PermissionRule>.Fail("Invalid path.");
        }

        var state = _store.Read();
        if (state.Repositories.All(r => r.Id != repositoryId))
        {
            return OperationResult<PermissionRule>.Fail("Repository not found.");
        }

        var subjectExists = subjectType switch
        {
            SubjectType.User => state.Users.Any(u => u.Id == subjectId),
            SubjectType.Group => state.Groups.Any(g => g.Id == subjectId),
            _ => false,
        };

        if (!subjectExists)
        {
            return OperationResult<PermissionRule>.Fail("Subject not found.");
        }

        var rule = new PermissionRule(
            Id: Guid.NewGuid(),
            RepositoryId: repositoryId,
            Path: normalizedPath,
            SubjectType: subjectType,
            SubjectId: subjectId,
            Access: access,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var newState = state with
        {
            PermissionRules = [..state.PermissionRules, rule],
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "permission.add",
                    Target: repositoryId.ToString("D"),
                    Success: true,
                    Details: $"{normalizedPath} {subjectType} {subjectId} {access}"
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

        return OperationResult<PermissionRule>.Ok(rule);
    }

    public async Task<OperationResult> DeleteRuleAsync(
        Guid actorUserId,
        Guid ruleId,
        CancellationToken cancellationToken = default
    )
    {
        var state = _store.Read();
        var existing = state.PermissionRules.FirstOrDefault(r => r.Id == ruleId);
        if (existing is null)
        {
            return OperationResult.Fail("Rule not found.");
        }

        var newState = state with
        {
            PermissionRules = state.PermissionRules.Where(r => r.Id != ruleId).ToList(),
            AuditEvents =
            [
                ..state.AuditEvents,
                new AuditEvent(
                    Id: Guid.NewGuid(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    ActorUserId: actorUserId,
                    Action: "permission.delete",
                    Target: existing.RepositoryId.ToString("D"),
                    Success: true,
                    Details: existing.Path
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

    private static string? NormalizeAuthzPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "/";
        }

        var p = input.Trim();
        if (!p.StartsWith('/'))
        {
            p = "/" + p;
        }

        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        if (p.Length > 1 && p.EndsWith('/'))
        {
            p = p.TrimEnd('/');
        }

        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part == "." || part == "..")
            {
                return null;
            }
        }

        return "/" + string.Join('/', parts);
    }
}
