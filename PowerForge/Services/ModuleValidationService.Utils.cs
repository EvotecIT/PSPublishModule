using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class ModuleValidationService
{
    private static ModuleValidationCheckResult BuildResult(
        string name,
        ValidationSeverity severity,
        List<string> issues,
        string summary)
    {
        var issueArray = issues?.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray() ?? Array.Empty<string>();
        var status = ResolveStatus(severity, issueArray.Length);
        return new ModuleValidationCheckResult(name, severity, status, summary, issueArray);
    }

    private static CheckStatus ResolveStatus(ValidationSeverity severity, int issueCount)
    {
        if (issueCount <= 0) return CheckStatus.Pass;
        return severity == ValidationSeverity.Error ? CheckStatus.Fail : CheckStatus.Warning;
    }

    private static double Percent(int part, int total)
    {
        if (total <= 0) return 0;
        return (part / (double)total) * 100.0;
    }

    private static bool TryLoadAssembly(string name, out string? error)
    {
        error = null;
        try
        {
            Assembly.Load(new AssemblyName(name));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static HashSet<string> DiscoverFunctionFileNames(string moduleRoot, string[]? paths)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (paths is null) return names;

        foreach (var rel in paths)
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            var full = Path.IsPathRooted(rel) ? rel : Path.Combine(moduleRoot, rel);
            if (!Directory.Exists(full)) continue;

            foreach (var file in Directory.EnumerateFiles(full, "*.ps1", SearchOption.AllDirectories))
                names.Add(Path.GetFileNameWithoutExtension(file));
        }

        return names;
    }

    private static (string[]? Values, bool Wildcard) GetManifestStringArray(string manifestPath, string key)
    {
        if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, key, out var values) && values is { Length: > 0 })
            return (values, false);

        if (ManifestEditor.TryGetTopLevelString(manifestPath, key, out var raw))
        {
            var trimmed = raw?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                if (string.Equals(trimmed, "*", StringComparison.OrdinalIgnoreCase))
                    return (Array.Empty<string>(), true);
                return (new[] { trimmed! }, false);
            }
        }

        return (null, false);
    }

    private static string[] ResolveManifestAssemblies(string manifestPath)
    {
        var list = new List<string>();
        if (ManifestEditor.TryGetTopLevelString(manifestPath, "RootModule", out var root) && !string.IsNullOrWhiteSpace(root))
            list.Add(root!);

        if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, "NestedModules", out var nested) && nested is { Length: > 0 })
            list.AddRange(nested.Where(n => !string.IsNullOrWhiteSpace(n)));

        if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, "RequiredAssemblies", out var assemblies) && assemblies is { Length: > 0 })
            list.AddRange(assemblies.Where(a => !string.IsNullOrWhiteSpace(a)));

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveModuleRoot(ModuleValidationSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.StagingPath)) return spec.StagingPath;
        if (!string.IsNullOrWhiteSpace(spec.ManifestPath))
            return Path.GetDirectoryName(spec.ManifestPath) ?? spec.ProjectRoot;
        return spec.ProjectRoot;
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern, string[]? excludeDirectories)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
        var exclude = excludeDirectories ?? Array.Empty<string>();

        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            if (IsUnderExcludedDirectory(file, exclude)) continue;
            yield return file;
        }
    }

    private static bool IsUnderExcludedDirectory(string path, string[] exclude)
    {
        if (exclude.Length == 0) return false;
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var dir in exclude)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            if (parts.Any(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line is null) yield break;
            yield return line;
        }
    }

    private static string EncodeLines(IEnumerable<string> lines)
    {
        var joined = string.Join("\n", (lines ?? Array.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(joined));
    }

    private static string? ExtractMarker(string text, string prefix)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line.Substring(prefix.Length).Trim();
        }
        return null;
    }

    private static string BuildScriptAnalyzerScript()
    {
        return EmbeddedScripts.Load("Scripts/Validation/Invoke-ScriptAnalyzer.ps1");
    }

    private static string FormatList(IEnumerable<string> items, int max = 8)
    {
        var list = items.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray();
        if (list.Length == 0) return string.Empty;
        if (list.Length <= max) return string.Join(", ", list);
        return string.Join(", ", list.Take(max)) + ", ...";
    }

    private sealed class ScriptAnalyzerIssue
    {
        public string? RuleName { get; set; }
        public string? Severity { get; set; }
        public string? Message { get; set; }
        public string? ScriptPath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }
}
