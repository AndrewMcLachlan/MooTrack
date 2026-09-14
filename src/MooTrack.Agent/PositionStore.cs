using System.Text.Json;

namespace MooTrack.Agent;

public sealed class PositionStore
{
    private readonly string _path;
    private readonly Dictionary<string, long> _positions;
    private readonly Lock _gate = new();

    public PositionStore(string path)
    {
        _path = path;
        _positions = Load(path);
    }

    public long? LastRecordId(string logName)
    {
        lock (_gate)
            return _positions.TryGetValue(logName, out var recordId) ? recordId : null;
    }

    // A torn bookmark must not be readable as a valid position: resuming from a
    // half-written offset would skip the events written while the agent was down.
    public void Save(string logName, long recordId)
    {
        lock (_gate)
        {
            _positions[logName] = recordId;

            var directory = Path.GetDirectoryName(_path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_positions));
            File.Move(temporary, _path, overwrite: true);
        }
    }

    private static Dictionary<string, long> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
