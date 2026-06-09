using System.Text.Json;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseQueueCheckpointSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public string Serialize<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, SerializerOptions);
    }

    public string SerializeTransition(string fromStage, string toStage, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromStage);
        ArgumentException.ThrowIfNullOrWhiteSpace(toStage);

        return Serialize(new Dictionary<string, string> {
            ["from"] = fromStage,
            ["to"] = toStage,
            ["updatedAtUtc"] = timestamp.ToString("O")
        });
    }

    public T? TryDeserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch
        {
            return default;
        }
    }

    public T? TryRead<T>(ReleaseQueueItem item, string expectedCheckpointKey)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCheckpointKey);

        if (!string.Equals(item.CheckpointKey, expectedCheckpointKey, StringComparison.OrdinalIgnoreCase))
        {
            return default;
        }

        return TryDeserialize<T>(item.CheckpointStateJson);
    }
}
