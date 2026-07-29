using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// JSON IO helpers for benchmark result artifacts.
/// </summary>
public static class BenchmarkJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
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
        string fullPath = ResolveWritePath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(value, Options);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.SequentialScan))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                       bufferSize: 64 * 1024,
                       leaveOpen: true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
#if NET8_0_OR_GREATER
                if (OperatingSystem.IsWindows())
                {
                    File.Replace(temporaryPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: false);
                }
                else
                {
                    UnixFileMode destinationMode = File.GetUnixFileMode(fullPath);
                    File.SetUnixFileMode(temporaryPath, destinationMode);
                    File.Move(temporaryPath, fullPath, overwrite: true);
                }
#else
                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
#endif
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static string ResolveWritePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
#if NET8_0_OR_GREATER
        var visited = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        return ResolvePathComponents(fullPath, path, visited);
#else
        return fullPath;
#endif
    }

#if NET8_0_OR_GREATER
    private static string ResolvePathComponents(
        string fullPath,
        string originalPath,
        ISet<string> visitedLinks)
    {
        string root = Path.GetPathRoot(fullPath)
                      ?? throw new IOException(
                          $"Unable to determine the root while resolving benchmark output path '{originalPath}'.");
        char[] separators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
            ? [Path.DirectorySeparatorChar]
            : [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
        string[] components = fullPath.Substring(root.Length)
            .Split(separators, StringSplitOptions.RemoveEmptyEntries);
        string currentPath = root;
        foreach (string component in components)
        {
            string candidate = Path.Combine(currentPath, component);
            string? linkTarget = GetLinkTarget(candidate);
            if (string.IsNullOrWhiteSpace(linkTarget))
            {
                currentPath = candidate;
                continue;
            }

            string normalizedLink = Path.GetFullPath(candidate);
            if (!visitedLinks.Add(normalizedLink))
                throw new IOException(
                    $"Symbolic-link cycle detected while resolving benchmark output path '{originalPath}'.");

            string targetPath = Path.GetFullPath(
                Path.IsPathRooted(linkTarget)
                    ? linkTarget
                    : Path.Combine(Path.GetDirectoryName(normalizedLink)!, linkTarget));
            currentPath = ResolvePathComponents(targetPath, originalPath, visitedLinks);
        }

        return Path.GetFullPath(currentPath);
    }

    private static string? GetLinkTarget(string path)
    {
        FileSystemInfo entry = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        return entry.LinkTarget;
    }
#endif

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

    /// <summary>
    /// Reads benchmark comparison rows from either a full run result or a comparison array.
    /// </summary>
    /// <param name="path">Comparison JSON path.</param>
    /// <returns>Comparison rows.</returns>
    public static BenchmarkComparisonRow[] ReadComparison(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<BenchmarkComparisonRow[]>(root.GetRawText(), Options) ?? Array.Empty<BenchmarkComparisonRow>();
        if (root.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(root, "comparison", out var comparison))
            return JsonSerializer.Deserialize<BenchmarkComparisonRow[]>(comparison.GetRawText(), Options) ?? Array.Empty<BenchmarkComparisonRow>();

        throw new InvalidOperationException($"Benchmark comparison JSON must be an array or contain a comparison property: {path}");
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
