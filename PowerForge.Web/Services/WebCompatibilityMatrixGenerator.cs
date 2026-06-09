using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Generates compatibility matrix outputs for .NET libraries and PowerShell modules.</summary>
public static class WebCompatibilityMatrixGenerator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex Psd1AssignmentRegex = new(
        @"(?ims)^\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.*?)(?=^\s*[A-Za-z_][A-Za-z0-9_]*\s*=|\z)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex QuotedValueRegex = new(
        @"'(?<single>[^']*)'|""(?<double>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex Psd1HashtableRegex = new(
        @"(?is)@\{(?<body>.*?)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex Psd1ModuleNameRegex = new(
        @"(?im)\bModuleName\s*=\s*('(?<single>[^']+)'|""(?<double>[^""]+)"")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex Psd1ModuleVersionRegex = new(
        @"(?im)\b(ModuleVersion|RequiredVersion|MaximumVersion)\s*=\s*('(?<single>[^']+)'|""(?<double>[^""]+)"")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex TrimWhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    /// <summary>Generates compatibility matrix JSON and optional markdown.</summary>
    /// <param name="options">Generation options.</param>
    /// <returns>Generation result.</returns>
    public static WebCompatibilityMatrixResult Generate(WebCompatibilityMatrixOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var warnings = new List<string>();
        var baseDirectory = ResolveBaseDirectory(options.BaseDirectory);
        var outputPath = ResolvePath(options.OutputPath, baseDirectory, warnings, "OutputPath");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("OutputPath is invalid.", nameof(options));

        string? markdownOutputPath = null;
        if (!string.IsNullOrWhiteSpace(options.MarkdownOutputPath))
            markdownOutputPath = ResolvePath(options.MarkdownOutputPath, baseDirectory, warnings, "MarkdownOutputPath");

        EnsureParentDirectory(outputPath);
        if (!string.IsNullOrWhiteSpace(markdownOutputPath))
            EnsureParentDirectory(markdownOutputPath);

        var entries = new Dictionary<string, WebCompatibilityMatrixEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in options.Entries ?? Enumerable.Empty<WebCompatibilityMatrixEntryInput>())
            AddOrMerge(entries, MapInputEntry(input, options.IncludeDependencies, warnings));

        foreach (var path in NormalizeInputPaths(options.CsprojFiles, baseDirectory))
            AddOrMerge(entries, LoadFromCsproj(path, options.IncludeDependencies, warnings));

        foreach (var path in NormalizeInputPaths(options.Psd1Files, baseDirectory))
            AddOrMerge(entries, LoadFromPsd1(path, options.IncludeDependencies, warnings));

        var orderedEntries = entries.Values
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Id))
            .OrderBy(entry => entry.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var document = new WebCompatibilityMatrixDocument
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Compatibility Matrix" : options.Title!.Trim(),
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Entries = orderedEntries
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, WebJson.Options), Encoding.UTF8);
        if (!string.IsNullOrWhiteSpace(markdownOutputPath))
            File.WriteAllText(markdownOutputPath, RenderMarkdown(document), Encoding.UTF8);

        return new WebCompatibilityMatrixResult
        {
            OutputPath = outputPath,
            MarkdownOutputPath = markdownOutputPath,
            EntryCount = orderedEntries.Count,
            Warnings = warnings
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static WebCompatibilityMatrixEntry? MapInputEntry(
        WebCompatibilityMatrixEntryInput? input,
        bool includeDependencies,
        List<string> warnings)
    {
        if (input is null)
            return null;

        var id = NormalizeValue(input.Id) ?? NormalizeValue(input.Name);
        if (string.IsNullOrWhiteSpace(id))
        {
            warnings.Add("Compatibility matrix input entry is missing Id/Name and was skipped.");
            return null;
        }

        return new WebCompatibilityMatrixEntry
        {
            Type = NormalizeType(input.Type),
            Id = id,
            Name = NormalizeValue(input.Name) ?? id,
            Version = NormalizeValue(input.Version),
            SourcePath = NormalizeValue(input.SourcePath),
            TargetFrameworks = NormalizeValues(input.TargetFrameworks),
            PowerShellEditions = NormalizeValues(input.PowerShellEditions),
            PowerShellVersion = NormalizeValue(input.PowerShellVersion),
            Dependencies = includeDependencies ? NormalizeValues(input.Dependencies) : new List<string>(),
            Status = NormalizeValue(input.Status),
            Notes = NormalizeValue(input.Notes),
            Url = NormalizeValue(input.Url)
        };
    }

    private static WebCompatibilityMatrixEntry? LoadFromCsproj(string path, bool includeDependencies, List<string> warnings)
    {
        if (!File.Exists(path))
        {
            warnings.Add($"Csproj input file not found: {path}");
            return null;
        }

        try
        {
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var packageId = ReadFirstCsprojValue(document, "PackageId");
            var assemblyName = ReadFirstCsprojValue(document, "AssemblyName");
            var version = ReadFirstCsprojValue(document, "Version") ?? ReadFirstCsprojValue(document, "VersionPrefix");
            var tfms = SplitList(ReadFirstCsprojValue(document, "TargetFrameworks"));
            if (tfms.Count == 0)
            {
                var tfm = NormalizeValue(ReadFirstCsprojValue(document, "TargetFramework"));
                if (!string.IsNullOrWhiteSpace(tfm))
                    tfms.Add(tfm);
            }

            var id = NormalizeValue(packageId) ?? NormalizeValue(assemblyName) ?? Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(id))
            {
                warnings.Add($"Unable to infer package id from csproj: {path}");
                return null;
            }

            var dependencies = new List<string>();
            if (includeDependencies)
            {
                foreach (var reference in document.Descendants().Where(e => e.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase)))
                {
                    var include = NormalizeValue(reference.Attribute("Include")?.Value) ??
                                  NormalizeValue(reference.Attribute("Update")?.Value);
                    if (string.IsNullOrWhiteSpace(include))
                        continue;

                    var dependencyVersion = NormalizeValue(reference.Attribute("Version")?.Value) ??
                                            NormalizeValue(reference.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Version", StringComparison.OrdinalIgnoreCase))?.Value);
                    dependencies.Add(string.IsNullOrWhiteSpace(dependencyVersion)
                        ? include
                        : $"{include} ({dependencyVersion})");
                }
            }

            return new WebCompatibilityMatrixEntry
            {
                Type = "nuget",
                Id = id,
                Name = NormalizeValue(assemblyName) ?? id,
                Version = NormalizeValue(version),
                SourcePath = path,
                TargetFrameworks = NormalizeValues(tfms),
                Dependencies = NormalizeValues(dependencies)
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse csproj '{path}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static WebCompatibilityMatrixEntry? LoadFromPsd1(string path, bool includeDependencies, List<string> warnings)
    {
        if (!File.Exists(path))
        {
            warnings.Add($"Psd1 input file not found: {path}");
            return null;
        }

        try
        {
            var content = File.ReadAllText(path);
            var assignments = ReadPsd1Assignments(content);
            var moduleVersion = GetScalarFromPsd1(assignments, "ModuleVersion");
            var powerShellVersion = GetScalarFromPsd1(assignments, "PowerShellVersion");
            var rootModule = GetScalarFromPsd1(assignments, "RootModule") ?? GetScalarFromPsd1(assignments, "ModuleToProcess");
            var editions = GetArrayFromPsd1(assignments, "CompatiblePSEditions");
            if (editions.Count == 0)
                editions = GetArrayFromPsd1(assignments, "PSEditions");

            var dependencies = new List<string>();
            if (includeDependencies)
                dependencies = GetRequiredModulesFromPsd1(assignments, warnings);

            var id = NormalizeValue(rootModule);
            if (!string.IsNullOrWhiteSpace(id))
                id = Path.GetFileNameWithoutExtension(id);
            if (string.IsNullOrWhiteSpace(id))
                id = Path.GetFileNameWithoutExtension(path);

            if (string.IsNullOrWhiteSpace(id))
            {
                warnings.Add($"Unable to infer module id from psd1: {path}");
                return null;
            }

            return new WebCompatibilityMatrixEntry
            {
                Type = "powershell-module",
                Id = id,
                Name = id,
                Version = moduleVersion,
                SourcePath = path,
                PowerShellEditions = NormalizeValues(editions),
                PowerShellVersion = NormalizeValue(powerShellVersion),
                Dependencies = NormalizeValues(dependencies)
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse psd1 '{path}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, string> ReadPsd1Assignments(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(content))
            return result;

        foreach (Match match in Psd1AssignmentRegex.Matches(content))
        {
            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                continue;

            result[key] = value;
        }

        return result;
    }

    private static string? GetScalarFromPsd1(IReadOnlyDictionary<string, string> assignments, string key)
    {
        if (!assignments.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        var first = ExtractQuotedValues(raw).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
            return NormalizeValue(first);

        var token = raw.Split(new[] { '\r', '\n', '#', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return null;
        return NormalizeValue(token.Trim().Trim('\'', '"', ','));
    }

    private static List<string> GetArrayFromPsd1(IReadOnlyDictionary<string, string> assignments, string key)
    {
        if (!assignments.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return new List<string>();
        return NormalizeValues(ExtractQuotedValues(raw));
    }

    private static List<string> GetRequiredModulesFromPsd1(IReadOnlyDictionary<string, string> assignments, List<string> warnings)
    {
        if (!assignments.TryGetValue("RequiredModules", out var raw) || string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        var modules = new List<string>();
        var hashtableSpans = new List<(int Start, int Length)>();

        foreach (Match block in Psd1HashtableRegex.Matches(raw))
        {
            if (!block.Success)
                continue;

            hashtableSpans.Add((block.Index, block.Length));
            var body = block.Groups["body"].Value;
            var moduleNameMatch = Psd1ModuleNameRegex.Match(body);
            if (!moduleNameMatch.Success)
                continue;

            var moduleName = NormalizeValue(moduleNameMatch.Groups["single"].Success
                ? moduleNameMatch.Groups["single"].Value
                : moduleNameMatch.Groups["double"].Value);
            if (string.IsNullOrWhiteSpace(moduleName))
                continue;

            var moduleVersionMatch = Psd1ModuleVersionRegex.Match(body);
            var moduleVersion = moduleVersionMatch.Success
                ? NormalizeValue(moduleVersionMatch.Groups["single"].Success
                    ? moduleVersionMatch.Groups["single"].Value
                    : moduleVersionMatch.Groups["double"].Value)
                : null;
            modules.Add(string.IsNullOrWhiteSpace(moduleVersion) ? moduleName : $"{moduleName} ({moduleVersion})");
        }

        var leftover = RemoveSpans(raw, hashtableSpans);
        foreach (var token in ExtractQuotedValues(leftover))
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;
            if (token.Equals("ModuleName", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("ModuleVersion", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("RequiredVersion", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("MaximumVersion", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            modules.Add(token);
        }

        var normalized = NormalizeValues(modules);
        if (normalized.Count == 0 && raw.Contains("RequiredModules", StringComparison.OrdinalIgnoreCase))
            warnings.Add("RequiredModules was present in psd1 but no module names could be parsed.");
        return normalized;
    }

    private static string RemoveSpans(string text, IReadOnlyList<(int Start, int Length)> spans)
    {
        if (string.IsNullOrWhiteSpace(text) || spans.Count == 0)
            return text;

        var ordered = spans.OrderByDescending(static span => span.Start).ToArray();
        var builder = new StringBuilder(text);
        foreach (var span in ordered)
        {
            if (span.Start < 0 || span.Length <= 0 || span.Start >= builder.Length)
                continue;
            var length = Math.Min(span.Length, builder.Length - span.Start);
            builder.Remove(span.Start, length);
        }
        return builder.ToString();
    }

    private static List<string> ExtractQuotedValues(string value)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
            return results;

        foreach (Match match in QuotedValueRegex.Matches(value))
        {
            if (!match.Success)
                continue;

            var token = match.Groups["single"].Success
                ? match.Groups["single"].Value
                : match.Groups["double"].Value;
            if (!string.IsNullOrWhiteSpace(token))
                results.Add(token.Trim());
        }

        return results;
    }

    private static string? ReadFirstCsprojValue(XDocument document, string elementName)
    {
        return document
            .Descendants()
            .Where(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
            .Select(element => NormalizeValue(element.Value))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static List<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeValue)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToList();
    }

    private static IEnumerable<string> NormalizeInputPaths(IEnumerable<string>? paths, string? baseDirectory)
    {
        if (paths is null)
            return Array.Empty<string>();

        return paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                var trimmed = path.Trim().Trim('"');
                if (Path.IsPathRooted(trimmed) || string.IsNullOrWhiteSpace(baseDirectory))
                    return Path.GetFullPath(trimmed);
                return Path.GetFullPath(Path.Combine(baseDirectory, trimmed));
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddOrMerge(IDictionary<string, WebCompatibilityMatrixEntry> entries, WebCompatibilityMatrixEntry? incoming)
    {
        if (incoming is null || string.IsNullOrWhiteSpace(incoming.Id))
            return;

        var key = $"{NormalizeType(incoming.Type)}|{incoming.Id.Trim()}";
        if (!entries.TryGetValue(key, out var current))
        {
            entries[key] = incoming;
            return;
        }

        if (string.IsNullOrWhiteSpace(current.Name))
            current.Name = incoming.Name;
        if (string.IsNullOrWhiteSpace(current.Version))
            current.Version = incoming.Version;
        if (string.IsNullOrWhiteSpace(current.SourcePath))
            current.SourcePath = incoming.SourcePath;
        if (string.IsNullOrWhiteSpace(current.PowerShellVersion))
            current.PowerShellVersion = incoming.PowerShellVersion;
        if (string.IsNullOrWhiteSpace(current.Status))
            current.Status = incoming.Status;
        if (string.IsNullOrWhiteSpace(current.Notes))
            current.Notes = incoming.Notes;
        if (string.IsNullOrWhiteSpace(current.Url))
            current.Url = incoming.Url;

        current.TargetFrameworks = NormalizeValues(current.TargetFrameworks.Concat(incoming.TargetFrameworks));
        current.PowerShellEditions = NormalizeValues(current.PowerShellEditions.Concat(incoming.PowerShellEditions));
        current.Dependencies = NormalizeValues(current.Dependencies.Concat(incoming.Dependencies));
    }

    private static string RenderMarkdown(WebCompatibilityMatrixDocument document)
    {
        var lines = new List<string>
        {
            $"# {document.Title}",
            string.Empty,
            $"Generated: {document.GeneratedAtUtc}",
            string.Empty
        };

        if (document.Entries.Count == 0)
        {
            lines.Add("_No compatibility entries found._");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add("| Type | Id | Version | Target Frameworks | PS Editions | PS Version | Dependencies | Status |");
        lines.Add("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var entry in document.Entries)
        {
            lines.Add(
                "| " +
                $"{EscapeMarkdown(entry.Type)} | " +
                $"{EscapeMarkdown(entry.Id)} | " +
                $"{EscapeMarkdown(entry.Version)} | " +
                $"{EscapeMarkdown(string.Join(", ", entry.TargetFrameworks))} | " +
                $"{EscapeMarkdown(string.Join(", ", entry.PowerShellEditions))} | " +
                $"{EscapeMarkdown(entry.PowerShellVersion)} | " +
                $"{EscapeMarkdown(string.Join(", ", entry.Dependencies))} | " +
                $"{EscapeMarkdown(entry.Status)} |");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeMarkdown(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string NormalizeType(string? value)
    {
        var normalized = NormalizeValue(value);
        return string.IsNullOrWhiteSpace(normalized) ? "other" : normalized.ToLowerInvariant();
    }

    private static List<string> NormalizeValues(IEnumerable<string>? values)
    {
        return (values ?? Enumerable.Empty<string>())
            .Select(NormalizeValue)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return TrimWhitespaceRegex.Replace(value.Trim(), " ");
    }

    private static string? ResolveBaseDirectory(string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return null;

        try
        {
            return Path.GetFullPath(baseDirectory.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePath(string? value, string? baseDirectory, List<string> warnings, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var trimmed = value.Trim().Trim('"');
            var full = Path.IsPathRooted(trimmed) || string.IsNullOrWhiteSpace(baseDirectory)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(baseDirectory, trimmed));
            return full;
        }
        catch (Exception ex)
        {
            warnings.Add($"{label} could not be resolved: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }
}
