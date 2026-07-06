using System.Text.Json;
using SvnHub.App.Configuration;
using SvnHub.App.Storage;
using SvnHub.Domain;

namespace SvnHub.Infrastructure.Storage;

public sealed class MultiFilePortalStore : IPortalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _dataDir;

    private readonly string _configPath;
    private readonly string _reposPath;
    private readonly string _usersPath;
    private readonly string _groupsPath;
    private readonly string _permissionsPath;
    private readonly string _repositoryManagementPath;
    private readonly string _apiTokensPath;
    private readonly string _auditPath;

    private PortalState _state;

    public MultiFilePortalStore(SvnHubOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dataDir = Path.GetFullPath(options.DataDirectory);
        Directory.CreateDirectory(_dataDir);

        _configPath = Path.Combine(_dataDir, "config.json");
        _reposPath = Path.Combine(_dataDir, "repos.json");
        _usersPath = Path.Combine(_dataDir, "users.json");
        _groupsPath = Path.Combine(_dataDir, "groups.json");
        _permissionsPath = Path.Combine(_dataDir, "permissions.json");
        _repositoryManagementPath = Path.Combine(_dataDir, "repository-management.json");
        _apiTokensPath = Path.Combine(_dataDir, "api-tokens.json");
        _auditPath = Path.Combine(_dataDir, "audit.json");

        _state = LoadOrCreate().Snapshot();
    }

    public PortalState Read()
    {
        lock (_gate)
        {
            return _state.Snapshot();
        }
    }

    public void Write(PortalState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            Save(state);
            _state = state.Snapshot();
        }
    }

    private PortalState LoadOrCreate()
    {
        var settings = ReadFileOrDefault(_configPath, static () => new PortalSettings()) ?? new PortalSettings();
        var repos = ReadFileOrDefault(_reposPath, static () => new List<Repository>());
        var users = ReadFileOrDefault(_usersPath, static () => new List<PortalUser>());
        var groupsBundle = ReadFileOrDefault(_groupsPath, static () => new GroupsBundle());
        var rules = ReadFileOrDefault(_permissionsPath, static () => new List<PermissionRule>());
        var managementGrants = ReadFileOrDefault(_repositoryManagementPath, static () => new List<RepositoryManagementGrant>());
        var apiTokens = ReadFileOrDefault(_apiTokensPath, static () => new List<ApiToken>());
        var audit = ReadFileOrDefault(_auditPath, static () => new List<AuditEvent>());
        var previousDefaultAccess = ReadPreviousDefaultAccess(_configPath);
        var previousRepositoryInheritance = ReadPreviousRepositoryInheritance(_reposPath);

        if (settings.RoleSchemaVersion < 1)
        {
            users = NormalizeUserRoles(users);
            settings = settings with { RoleSchemaVersion = 1 };
        }

        if (settings.RoleSchemaVersion < 2)
        {
            users = MigrateGlobalRepositoryGrants(users, previousDefaultAccess ?? AccessLevel.Write);
            repos = MigrateRepositoryGrantInheritance(repos, previousRepositoryInheritance);
            settings = settings with { RoleSchemaVersion = 2 };
        }

        if (settings.RoleSchemaVersion < 3)
        {
            repos = MigrateSplitRepositoryGrantInheritance(repos, previousRepositoryInheritance);
            settings = settings with { RoleSchemaVersion = 3 };
        }

        if (settings.RoleSchemaVersion < 4)
        {
            settings = settings with { RoleSchemaVersion = 4 };
        }

        SplitLegacyManagementRules(rules, managementGrants);
        managementGrants = NormalizeRepositoryManagementGrants(managementGrants);

        return PortalState.Empty() with
        {
            Repositories = repos,
            Users = users,
            Groups = groupsBundle.Groups ?? [],
            GroupMembers = groupsBundle.GroupMembers ?? [],
            GroupGroupMembers = groupsBundle.GroupGroupMembers ?? [],
            PermissionRules = rules.Where(r => IsContentAccess(r.Access)).ToList(),
            RepositoryManagementGrants = managementGrants,
            ApiTokens = apiTokens,
            AuditEvents = audit,
            Settings = settings,
        };
    }

    private void Save(PortalState state)
    {
        WriteFileAtomic(_configPath, state.Settings);
        WriteFileAtomic(_reposPath, state.Repositories);
        WriteFileAtomic(_usersPath, state.Users);
        WriteFileAtomic(_groupsPath, new GroupsBundle
        {
            Groups = state.Groups,
            GroupMembers = state.GroupMembers,
            GroupGroupMembers = state.GroupGroupMembers,
        });
        WriteFileAtomic(_permissionsPath, state.PermissionRules);
        WriteFileAtomic(_repositoryManagementPath, state.RepositoryManagementGrants);
        WriteFileAtomic(_apiTokensPath, state.ApiTokens);
        WriteFileAtomic(_auditPath, state.AuditEvents);
    }

    private static T ReadFileOrDefault<T>(string path, Func<T> factory)
    {
        try
        {
            if (!File.Exists(path))
            {
                return factory();
            }

            var json = File.ReadAllText(path);
            var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return value is null ? factory() : value;
        }
        catch
        {
            return factory();
        }
    }

    private static AccessLevel? ReadPreviousDefaultAccess(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return TryGetProperty(doc.RootElement, "defaultAuthenticatedAccess", out var value)
                ? ReadAccessLevel(value)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<Guid, PreviousRepositoryInheritance> ReadPreviousRepositoryInheritance(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<Guid, PreviousRepositoryInheritance>();
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new Dictionary<Guid, PreviousRepositoryInheritance>();
            }

            var result = new Dictionary<Guid, PreviousRepositoryInheritance>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!TryGetProperty(item, "id", out var idElement) ||
                    idElement.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(idElement.GetString(), out var id))
                {
                    continue;
                }

                AccessLevel? defaultAccess = null;
                if (TryGetProperty(item, "authenticatedDefaultAccess", out var accessElement))
                {
                    defaultAccess = ReadAccessLevel(accessElement);
                }

                bool? singleInheritanceFlag = null;
                if (TryGetProperty(item, "includeInheritedRepositoryGrants", out var includeElement) &&
                    includeElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    singleInheritanceFlag = includeElement.GetBoolean();
                }

                result[id] = new PreviousRepositoryInheritance(
                    defaultAccess,
                    singleInheritanceFlag);
            }

            return result;
        }
        catch
        {
            return new Dictionary<Guid, PreviousRepositoryInheritance>();
        }
    }

    private static bool TryGetProperty(JsonElement element, string camelCaseName, out JsonElement value)
    {
        if (element.TryGetProperty(camelCaseName, out value))
        {
            return true;
        }

        var pascalCaseName = char.ToUpperInvariant(camelCaseName[0]) + camelCaseName[1..];
        return element.TryGetProperty(pascalCaseName, out value);
    }

    private static AccessLevel? ReadAccessLevel(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) && Enum.IsDefined(typeof(AccessLevel), number) =>
                (AccessLevel)number,
            JsonValueKind.String when Enum.TryParse<AccessLevel>(value.GetString(), ignoreCase: true, out var parsed) =>
                parsed,
            _ => null,
        };
    }

    private static void WriteFileAtomic<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        AtomicFileWriter.WriteAllText(path, json);
    }

    private static List<PortalUser> NormalizeUserRoles(List<PortalUser> users)
    {
        if (users.Count == 0)
        {
            return users;
        }

        var normalized = users
            .Select(u => u with { Roles = u.Roles.NormalizeLegacyRoles() })
            .ToList();

        if (normalized.Any(u => u.IsActive && u.Roles.HasFlag(PortalUserRoles.Owner)))
        {
            return normalized;
        }

        var firstSystemAdmin = normalized.FirstOrDefault(u =>
            u.IsActive && u.Roles.HasFlag(PortalUserRoles.AdminSystem));
        if (firstSystemAdmin is null)
        {
            return normalized;
        }

        return normalized
            .Select(u => u.Id == firstSystemAdmin.Id
                ? u with { Roles = u.Roles | PortalUserRoles.Owner }
                : u)
            .ToList();
    }

    private static List<PortalUser> MigrateGlobalRepositoryGrants(
        List<PortalUser> users,
        AccessLevel defaultAccess)
    {
        if (users.Count == 0)
        {
            return users;
        }

        return users
            .Select(user =>
            {
                var roles = user.Roles;

                if (roles.HasFlag(PortalUserRoles.RepoAdmin))
                {
                    roles |= PortalUserRoles.RepoCreate;
                }

                if (user.IsActive)
                {
                    roles |= defaultAccess switch
                    {
                        AccessLevel.Write => PortalUserRoles.RepoWrite,
                        AccessLevel.Read => PortalUserRoles.RepoRead,
                        _ => PortalUserRoles.None,
                    };
                }

                return user with { Roles = roles };
            })
            .ToList();
    }

    private static List<Repository> MigrateRepositoryGrantInheritance(
        List<Repository> repos,
        IReadOnlyDictionary<Guid, PreviousRepositoryInheritance> previousInheritance)
    {
        if (repos.Count == 0)
        {
            return repos;
        }

        return repos
            .Select(repo =>
            {
                previousInheritance.TryGetValue(repo.Id, out var previous);
                var includeInherited = previous?.DefaultAccess != AccessLevel.None;

                return repo with
                {
                    IncludeInheritedContentGrants = includeInherited,
                    IncludeInheritedManagementGrants = includeInherited,
                };
            })
            .ToList();
    }

    private static List<Repository> MigrateSplitRepositoryGrantInheritance(
        List<Repository> repos,
        IReadOnlyDictionary<Guid, PreviousRepositoryInheritance> previousInheritance)
    {
        if (repos.Count == 0)
        {
            return repos;
        }

        return repos
            .Select(repo =>
            {
                if (!previousInheritance.TryGetValue(repo.Id, out var previous) ||
                    previous.SingleInheritanceFlag is not { } inherited)
                {
                    return repo;
                }

                return repo with
                {
                    IncludeInheritedContentGrants = inherited,
                    IncludeInheritedManagementGrants = inherited,
                };
            })
            .ToList();
    }

    private sealed class GroupsBundle
    {
        public List<Group>? Groups { get; set; }
        public List<GroupMember>? GroupMembers { get; set; }
        public List<GroupGroupMember>? GroupGroupMembers { get; set; }
    }

    private static void SplitLegacyManagementRules(
        List<PermissionRule> rules,
        List<RepositoryManagementGrant> managementGrants)
    {
        if (rules.Count == 0)
        {
            return;
        }

        var existing = managementGrants
            .Select(g => (g.RepositoryId, g.SubjectType, g.SubjectId))
            .ToHashSet();

        foreach (var rule in rules.Where(r => IsLegacyManagementAccess(r.Access) && r.Path == "/"))
        {
            var key = (rule.RepositoryId, rule.SubjectType, rule.SubjectId);
            if (!existing.Add(key))
            {
                continue;
            }

            managementGrants.Add(new RepositoryManagementGrant(
                Id: Guid.NewGuid(),
                RepositoryId: rule.RepositoryId,
                SubjectType: rule.SubjectType,
                SubjectId: rule.SubjectId,
                Role: RepositoryManagementRole.Admin,
                CreatedAt: rule.CreatedAt));
        }
    }

    private static List<RepositoryManagementGrant> NormalizeRepositoryManagementGrants(
        List<RepositoryManagementGrant> managementGrants) =>
        managementGrants
            .Select(g => g.Role == RepositoryManagementRole.Admin ? g : g with { Role = RepositoryManagementRole.Admin })
            .ToList();

    private static bool IsContentAccess(AccessLevel access) =>
        access is AccessLevel.None or AccessLevel.Read or AccessLevel.Write;

    private static bool IsLegacyManagementAccess(AccessLevel access) =>
        (int)access >= 3;

    private sealed record PreviousRepositoryInheritance(
        AccessLevel? DefaultAccess,
        bool? SingleInheritanceFlag);
}
