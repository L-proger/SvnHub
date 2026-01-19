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
        var settings = ReadFileOrDefault(_configPath, static () => new PortalSettings());
        var repos = ReadFileOrDefault(_reposPath, static () => new List<Repository>());
        var users = ReadFileOrDefault(_usersPath, static () => new List<PortalUser>());
        var groupsBundle = ReadFileOrDefault(_groupsPath, static () => new GroupsBundle());
        var rules = ReadFileOrDefault(_permissionsPath, static () => new List<PermissionRule>());
        var audit = ReadFileOrDefault(_auditPath, static () => new List<AuditEvent>());

        return PortalState.Empty() with
        {
            Repositories = repos,
            Users = users,
            Groups = groupsBundle.Groups ?? [],
            GroupMembers = groupsBundle.GroupMembers ?? [],
            GroupGroupMembers = groupsBundle.GroupGroupMembers ?? [],
            PermissionRules = rules,
            AuditEvents = audit,
            Settings = settings ?? new PortalSettings(),
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

    private static void WriteFileAtomic<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        AtomicFileWriter.WriteAllText(path, json);
    }

    private sealed class GroupsBundle
    {
        public List<Group>? Groups { get; set; }
        public List<GroupMember>? GroupMembers { get; set; }
        public List<GroupGroupMember>? GroupGroupMembers { get; set; }
    }
}

