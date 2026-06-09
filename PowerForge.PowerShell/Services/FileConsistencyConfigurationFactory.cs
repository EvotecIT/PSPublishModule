using System.Collections;

namespace PowerForge;

internal sealed class FileConsistencyConfigurationFactory
{
    public ConfigurationFileConsistencySegment Create(FileConsistencyConfigurationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return new ConfigurationFileConsistencySegment
        {
            Settings = new FileConsistencySettings
            {
                Enable = request.Enable,
                FailOnInconsistency = request.FailOnInconsistency,
                Severity = request.Severity,
                RequiredEncoding = request.RequiredEncoding,
                RequiredLineEnding = request.RequiredLineEnding,
                ProjectKind = request.ProjectKindSpecified ? request.ProjectKind : null,
                IncludePatterns = request.IncludePatterns,
                Scope = request.ScopeSpecified ? request.Scope : null,
                AutoFix = request.AutoFix,
                CreateBackups = request.CreateBackups,
                MaxInconsistencyPercentage = request.MaxInconsistencyPercentage,
                ExcludeDirectories = request.ExcludeDirectories ?? Array.Empty<string>(),
                ExcludeFiles = request.ExcludeFiles ?? Array.Empty<string>(),
                EncodingOverrides = ParseEncodingOverrides(request.EncodingOverrides),
                LineEndingOverrides = ParseLineEndingOverrides(request.LineEndingOverrides),
                UpdateProjectRoot = request.UpdateProjectRoot,
                ExportReport = request.ExportReport,
                ReportFileName = request.ReportFileName,
                CheckMixedLineEndings = request.CheckMixedLineEndings,
                CheckMissingFinalNewline = request.CheckMissingFinalNewline
            }
        };
    }

    private static Dictionary<string, FileConsistencyEncoding>? ParseEncodingOverrides(Hashtable? overrides)
    {
        if (overrides is not { Count: > 0 })
            return null;

        var resolved = new Dictionary<string, FileConsistencyEncoding>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in overrides)
        {
            var trimmedKey = entry.Key?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedKey))
                continue;

            var key = trimmedKey!;
            if (entry.Value is FileConsistencyEncoding encoding)
            {
                resolved[key] = encoding;
                continue;
            }

            if (entry.Value is string text &&
                Enum.TryParse<FileConsistencyEncoding>(text.Trim(), ignoreCase: true, out var parsed))
            {
                resolved[key] = parsed;
                continue;
            }

            throw new ArgumentException(
                $"EncodingOverrides value for '{key}' must be a FileConsistencyEncoding or string.",
                nameof(overrides));
        }

        return resolved.Count == 0 ? null : resolved;
    }

    private static Dictionary<string, FileConsistencyLineEnding>? ParseLineEndingOverrides(Hashtable? overrides)
    {
        if (overrides is not { Count: > 0 })
            return null;

        var resolved = new Dictionary<string, FileConsistencyLineEnding>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in overrides)
        {
            var trimmedKey = entry.Key?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedKey))
                continue;

            var key = trimmedKey!;
            if (entry.Value is FileConsistencyLineEnding lineEnding)
            {
                resolved[key] = lineEnding;
                continue;
            }

            if (entry.Value is string text &&
                Enum.TryParse<FileConsistencyLineEnding>(text.Trim(), ignoreCase: true, out var parsed))
            {
                resolved[key] = parsed;
                continue;
            }

            throw new ArgumentException(
                $"LineEndingOverrides value for '{key}' must be a FileConsistencyLineEnding or string.",
                nameof(overrides));
        }

        return resolved.Count == 0 ? null : resolved;
    }
}
