using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

internal static class BuildDiagnosticsBaselineStore
{
    private const long MaxBaselineFileSizeBytes = 10 * 1024 * 1024;
    private static readonly StringComparison PathComparison = Path.DirectorySeparatorChar == '\\'
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    internal static BuildDiagnosticsBaselineComparison? Evaluate(
        string projectRoot,
        ModulePipelineDiagnosticsOptions? options,
        BuildDiagnostic[] diagnostics)
    {
        if (options is null)
            return null;

        var shouldProcess = options.GenerateBaseline || options.UpdateBaseline || !string.IsNullOrWhiteSpace(options.BaselinePath);
        if (!shouldProcess)
            return null;

        var resolvedPath = ResolveBaselinePath(projectRoot, options.BaselinePath);
        var relevantDiagnostics = (diagnostics ?? Array.Empty<BuildDiagnostic>())
            .Where(static d => d is not null && d.Severity != BuildDiagnosticSeverity.Info)
            .ToArray();

        foreach (var diagnostic in relevantDiagnostics)
            diagnostic.BaselineKey = CreateBaselineKey(diagnostic);

        var currentKeys = new HashSet<string>(
            relevantDiagnostics
                .Select(static diagnostic => diagnostic.BaselineKey)
                .Where(static key => !string.IsNullOrWhiteSpace(key)),
            StringComparer.OrdinalIgnoreCase);

        var baselineLoaded = TryLoadKeys(resolvedPath, out var baselineKeys);
        var baselineSet = new HashSet<string>(baselineKeys, StringComparer.OrdinalIgnoreCase);

        foreach (var diagnostic in relevantDiagnostics)
        {
            if (string.IsNullOrWhiteSpace(diagnostic.BaselineKey))
                continue;

            diagnostic.BaselineState = baselineSet.Contains(diagnostic.BaselineKey)
                ? BuildDiagnosticBaselineState.Existing
                : BuildDiagnosticBaselineState.New;
        }

        var comparison = new BuildDiagnosticsBaselineComparison
        {
            BaselinePath = resolvedPath,
            BaselineLoaded = baselineLoaded,
            BaselineDiagnosticCount = baselineSet.Count,
            CurrentDiagnosticCount = currentKeys.Count,
            ExistingDiagnosticCount = currentKeys.Count(key => baselineSet.Contains(key)),
            NewDiagnosticCount = currentKeys.Count(key => !baselineSet.Contains(key)),
            ResolvedDiagnosticCount = baselineSet.Count(key => !currentKeys.Contains(key))
        };

        if (options.GenerateBaseline || options.UpdateBaseline)
        {
            WriteBaseline(resolvedPath, currentKeys);
            comparison.BaselineGenerated = options.GenerateBaseline;
            comparison.BaselineUpdated = options.UpdateBaseline;
            comparison.BaselineDiagnosticCount = currentKeys.Count;
        }

        return comparison;
    }

    internal static string ResolveBaselinePath(string projectRoot, string? baselinePath)
    {
        var candidate = string.IsNullOrWhiteSpace(baselinePath)
            ? ".powerforge/module-diagnostics-baseline.json"
            : baselinePath!.Trim();
        var normalizedRoot = NormalizeDirectoryPath(projectRoot);
        var resolvedPath = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(Path.Combine(normalizedRoot, candidate));
        if (!IsWithinRoot(normalizedRoot, resolvedPath))
            throw new InvalidOperationException($"Baseline path must resolve under project root: {candidate}");
        return resolvedPath;
    }

    internal static string CreateBaselineKey(BuildDiagnostic diagnostic)
    {
        if (diagnostic is null)
            return string.Empty;

        var normalizedPath = (diagnostic.SourcePath ?? string.Empty).Trim().Replace('\\', '/');
        var payload = string.Join("|", new[]
        {
            diagnostic.RuleId ?? string.Empty,
            diagnostic.Area.ToString(),
            diagnostic.Scope.ToString(),
            diagnostic.Owner.ToString(),
            diagnostic.RemediationKind.ToString(),
            normalizedPath,
            diagnostic.Details ?? string.Empty,
            diagnostic.GeneratedBy ?? string.Empty
        });

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return string.Concat(hash.Select(static b => b.ToString("X2")));
    }

    private static void WriteBaseline(string resolvedPath, IEnumerable<string> keys)
    {
        var payload = new
        {
            version = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            diagnosticCount = keys.Count(),
            diagnosticKeys = keys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase).ToArray()
        };

        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(resolvedPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static bool TryLoadKeys(string resolvedPath, out string[] keys)
    {
        keys = Array.Empty<string>();
        try
        {
            if (!File.Exists(resolvedPath))
                return false;

            var info = new FileInfo(resolvedPath);
            if (info.Length > MaxBaselineFileSizeBytes)
                return false;

            using var stream = File.OpenRead(resolvedPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (!TryGetPropertyIgnoreCase(root, "diagnosticKeys", out var diagnosticKeys) || diagnosticKeys.ValueKind != JsonValueKind.Array)
                return false;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in diagnosticKeys.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;

                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    set.Add(value!.Trim());
            }

            keys = set.ToArray();
            return true;
        }
        catch
        {
            keys = Array.Empty<string>();
            return false;
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(rootPath, PathComparison);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out value))
                return true;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
