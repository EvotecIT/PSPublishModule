using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// JSON IO helpers for benchmark result artifacts.
/// </summary>
public static class BenchmarkJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Writes a JSON artifact as UTF-8 without BOM.
    /// </summary>
    /// <typeparam name="T">Payload type.</typeparam>
    /// <param name="path">Output path.</param>
    /// <param name="value">Payload value.</param>
    public static void Write<T>(string path, T value)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Output path is required.", nameof(path));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = JsonSerializer.Serialize(value, Options);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Reads a JSON artifact.
    /// </summary>
    /// <typeparam name="T">Payload type.</typeparam>
    /// <param name="path">Input path.</param>
    /// <returns>Deserialized payload.</returns>
    public static T Read<T>(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Input path is required.", nameof(path));
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, Options)
               ?? throw new InvalidOperationException($"Unable to deserialize benchmark JSON: {path}");
    }

    /// <summary>
    /// Reads a benchmark summary from either a full run result or a summary array.
    /// </summary>
    /// <param name="path">Summary JSON path.</param>
    /// <returns>Summary rows.</returns>
    public static BenchmarkSummaryRow[] ReadSummary(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<BenchmarkSummaryRow[]>(root.GetRawText(), Options) ?? Array.Empty<BenchmarkSummaryRow>();
        if (root.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(root, "summary", out var summary))
            return JsonSerializer.Deserialize<BenchmarkSummaryRow[]>(summary.GetRawText(), Options) ?? Array.Empty<BenchmarkSummaryRow>();

        throw new InvalidOperationException($"Benchmark summary JSON must be an array or contain a summary property: {path}");
    }

    internal static bool TryGetPropertyIgnoreCase(JsonElement node, string propertyName, out JsonElement value)
    {
        value = default;
        if (node.ValueKind != JsonValueKind.Object) return false;
        foreach (var prop in node.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }
}
