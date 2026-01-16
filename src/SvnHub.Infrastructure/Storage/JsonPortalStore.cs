using System.Text.Json;
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
            return JsonSerializer.Deserialize<PortalState>(jsonText, JsonOptions) ?? PortalState.Empty();
        }
        catch
        {
            // MVP: if the state can't be read, fall back to empty (admin can restore from backup).
            return PortalState.Empty();
        }
    }
}
