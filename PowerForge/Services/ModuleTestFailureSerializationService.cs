using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Serializes module test failure analysis results.
/// </summary>
public sealed class ModuleTestFailureSerializationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Converts a failure analysis object to JSON.
    /// </summary>
    public string ToJson(ModuleTestFailureAnalysis analysis)
    {
        if (analysis is null)
            throw new ArgumentNullException(nameof(analysis));

        return JsonSerializer.Serialize(analysis, JsonOptions);
    }
}
