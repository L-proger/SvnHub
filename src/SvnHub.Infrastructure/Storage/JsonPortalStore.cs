using System.Text.Json;
using System.Text.Json.Nodes;
using SvnHub.App.Configuration;
using SvnHub.App.Storage;
using SvnHub.Domain;

namespace SvnHub.Infrastructure.Storage;

public sealed class JsonPortalStore : IPortalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _dataFilePath;

    private PortalState _state;

    public JsonPortalStore(SvnHubOptions options)
    {
        _dataFilePath = Path.GetFullPath(options.DataFilePath);
        _state = LoadOrCreate(_dataFilePath).Snapshot();
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
            var json = JsonSerializer.Serialize(state, JsonOptions);
            AtomicFileWriter.WriteAllText(_dataFilePath, json);
            _state = state.Snapshot();
        }
    }

    private static PortalState LoadOrCreate(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                var empty = PortalState.Empty();
                var json = JsonSerializer.Serialize(empty, JsonOptions);
                AtomicFileWriter.WriteAllText(path, json);
                return empty;
            }

            var jsonText = File.ReadAllText(path);
            var migrated = TryMigrate(jsonText);
            var state = JsonSerializer.Deserialize<PortalState>(migrated, JsonOptions) ?? PortalState.Empty();

            if (!string.Equals(jsonText, migrated, StringComparison.Ordinal))
            {
                AtomicFileWriter.WriteAllText(path, migrated);
            }

            return state;
        }
        catch
        {
            // MVP: if the state can't be read, fall back to empty (admin can restore from backup).
            return PortalState.Empty();
        }
    }

    private static string TryMigrate(string json)
    {
        try
        {
            var node = JsonNode.Parse(json) as JsonObject;
            if (node is null)
            {
                return json;
            }

            if (node["users"] is not JsonArray users)
            {
                return json;
            }

            var changed = false;
            foreach (var u in users.OfType<JsonObject>())
            {
                if (u.ContainsKey("roles"))
                {
                    continue;
                }

                if (u.TryGetPropertyValue("role", out var legacyRoleNode) && legacyRoleNode is not null)
                {
                    // Legacy PortalRole: User=0, Admin=1
                    var legacyRole = legacyRoleNode.GetValue<int>();
                    var roles = legacyRole == 1 ? (int)PortalUserRoles.AllAdmin : (int)PortalUserRoles.None;
                    u["roles"] = roles;
                    u.Remove("role");
                    changed = true;
                }
            }

            return changed ? node.ToJsonString(JsonOptions) : json;
        }
        catch
        {
            return json;
        }
    }
}
