using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;

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
        WriteBytes(path, SerializeCanonicalBytes(value));
    }

    internal static void WriteBytes(string path, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Output path is required.", nameof(path));
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        string fullPath = ResolveWritePath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            bool clonedUnixMetadata = false;
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows() && File.Exists(fullPath))
            {
                CloneUnixFileForReplacement(fullPath, temporaryPath);
                clonedUnixMetadata = true;
            }
#endif
            using (var stream = new FileStream(
                       temporaryPath,
                       clonedUnixMetadata ? FileMode.Truncate : FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.SequentialScan))
            {
                stream.Write(bytes, 0, bytes.Length);
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

#if NET8_0_OR_GREATER
    private static void CloneUnixFileForReplacement(
        string sourcePath,
        string destinationPath)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Unix metadata cloning is not available on Windows.");
        string? executable = ResolveUnixCopyExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new PlatformNotSupportedException(
                $"A metadata-preserving 'cp' executable is required to atomically replace '{sourcePath}' without losing Unix ownership or ACLs.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsLinux())
            startInfo.ArgumentList.Add("--preserve=all");
        else
            startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add(destinationPath);

        using Process process = Process.Start(startInfo)
                                ?? throw new IOException(
                                    $"Unable to start metadata-preserving copy for '{sourcePath}'.");
        string standardError = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException(
                $"Unable to preserve Unix ownership and ACL metadata while replacing '{sourcePath}': {standardError.Trim()}");
        }
    }

    private static string? ResolveUnixCopyExecutable()
    {
        foreach (string candidate in new[] { "/bin/cp", "/usr/bin/cp" })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;
        foreach (string directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            string candidate = Path.Combine(directory, "cp");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
#endif

    internal static string ComputeSha256<T>(T value)
    {
        byte[] bytes = SerializeCanonicalBytes(value);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(bytes))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static byte[] SerializeCanonicalBytes<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, Options)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    }

    internal static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
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
