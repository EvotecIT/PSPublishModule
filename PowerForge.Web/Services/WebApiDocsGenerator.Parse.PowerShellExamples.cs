using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static readonly Regex PowerShellCommandTokenRegex = new(
        @"\b[A-Za-z][A-Za-z0-9]*-[A-Za-z0-9][A-Za-z0-9_.-]*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static void AppendPowerShellFallbackExamples(
        ApiDocModel apiDoc,
        string inputHelpPath,
        string resolvedHelpPath,
        string? manifestPath,
        WebApiDocsOptions options,
        List<string> warnings)
    {
        if (apiDoc is null || options is null || !options.GeneratePowerShellFallbackExamples)
            return;

        var limit = Math.Clamp(options.PowerShellFallbackExampleLimitPerCommand, 1, 5);
        var commandTypes = apiDoc.Types.Values
            .Where(IsPowerShellCommandType)
            .Where(static type => !HasCodeExample(type))
            .OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (commandTypes.Count == 0)
            return;

        var commandNames = commandTypes.Select(type => type.Name).ToArray();
        var files = ResolvePowerShellExampleScriptFiles(inputHelpPath, resolvedHelpPath, manifestPath, options, warnings);
        var snippetsByCommand = CollectPowerShellExamplesFromScripts(files, commandNames, limit, warnings);

        foreach (var type in commandTypes)
        {
            if (snippetsByCommand.TryGetValue(type.Name, out var snippets) && snippets.Count > 0)
            {
                foreach (var snippet in snippets.Take(limit))
                {
                    type.Examples.Add(new ApiExampleModel
                    {
                        Kind = "code",
                        Text = snippet
                    });
                }
                continue;
            }

            var fallback = BuildGeneratedPowerShellExample(type);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "text",
                    Text = "Generated fallback example from command syntax."
                });
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "code",
                    Text = fallback
                });
            }
        }
    }

    private static bool IsPowerShellCommandType(ApiTypeModel type)
    {
        if (type is null || string.IsNullOrWhiteSpace(type.Name))
            return false;
        if (type.Name.StartsWith("about_", StringComparison.OrdinalIgnoreCase))
            return false;

        return type.Kind.Equals("Cmdlet", StringComparison.OrdinalIgnoreCase) ||
               type.Kind.Equals("Function", StringComparison.OrdinalIgnoreCase) ||
               type.Kind.Equals("Alias", StringComparison.OrdinalIgnoreCase) ||
               type.Kind.Equals("Command", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCodeExample(ApiTypeModel type)
        => type.Examples.Any(ex =>
            ex is not null &&
            ex.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ex.Text));

    private static IReadOnlyList<string> ResolvePowerShellExampleScriptFiles(
        string inputHelpPath,
        string resolvedHelpPath,
        string? manifestPath,
        WebApiDocsOptions options,
        List<string> warnings)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.PowerShellExamplesPath))
        {
            var explicitPath = Path.GetFullPath(options.PowerShellExamplesPath);
            if (File.Exists(explicitPath) && explicitPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(explicitPath);
            }
            else if (Directory.Exists(explicitPath))
            {
                foreach (var file in SafeEnumerateFiles(explicitPath, "*.ps1", SearchOption.AllDirectories))
                    files.Add(file);
            }
            else
            {
                warnings?.Add($"API docs coverage: PowerShell examples path not found: {options.PowerShellExamplesPath}");
            }
        }

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(inputHelpPath) && Directory.Exists(inputHelpPath))
            roots.Add(Path.GetFullPath(inputHelpPath));

        var helpDir = Path.GetDirectoryName(resolvedHelpPath);
        if (!string.IsNullOrWhiteSpace(helpDir) && Directory.Exists(helpDir))
        {
            roots.Add(Path.GetFullPath(helpDir));
            var parent = Directory.GetParent(helpDir);
            if (parent is not null && parent.Exists)
                roots.Add(parent.FullName);
        }

        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            var manifestDir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrWhiteSpace(manifestDir) && Directory.Exists(manifestDir))
                roots.Add(Path.GetFullPath(manifestDir));
        }

        foreach (var root in roots)
        {
            if (Path.GetFileName(root).Equals("examples", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var file in SafeEnumerateFiles(root, "*.ps1", SearchOption.AllDirectories))
                    files.Add(file);
                continue;
            }

            foreach (var dir in SafeEnumerateDirectories(root, "Examples", SearchOption.AllDirectories))
            {
                foreach (var file in SafeEnumerateFiles(dir, "*.ps1", SearchOption.AllDirectories))
                    files.Add(file);
            }
        }

        return files.OrderBy(static f => f, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, List<string>> CollectPowerShellExamplesFromScripts(
        IReadOnlyList<string> files,
        IReadOnlyList<string> commandNames,
        int maxPerCommand,
        List<string> warnings)
    {
        var results = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var dedupe = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (files.Count == 0 || commandNames.Count == 0)
            return results;

        var commands = new HashSet<string>(commandNames, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (Exception ex)
            {
                warnings?.Add($"API docs coverage: skipped PowerShell examples file '{Path.GetFileName(file)}' ({ex.GetType().Name}: {ex.Message})");
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                    continue;

                foreach (Match match in PowerShellCommandTokenRegex.Matches(line))
                {
                    var command = match.Value;
                    if (!commands.Contains(command))
                        continue;

                    if (results.TryGetValue(command, out var existing) && existing.Count >= maxPerCommand)
                        continue;

                    var snippet = CapturePowerShellExampleSnippet(lines, i);
                    if (string.IsNullOrWhiteSpace(snippet))
                        continue;

                    if (!dedupe.TryGetValue(command, out var dedupeSet))
                    {
                        dedupeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        dedupe[command] = dedupeSet;
                    }

                    if (!dedupeSet.Add(snippet))
                        continue;

                    if (!results.TryGetValue(command, out var snippets))
                    {
                        snippets = new List<string>();
                        results[command] = snippets;
                    }

                    snippets.Add(snippet);
                }
            }
        }

        return results;
    }

    private static string CapturePowerShellExampleSnippet(string[] lines, int startIndex)
    {
        if (lines is null || startIndex < 0 || startIndex >= lines.Length)
            return string.Empty;

        var startLine = lines[startIndex];
        if (string.IsNullOrWhiteSpace(startLine))
            return string.Empty;

        var snippet = new List<string> { startLine.TrimEnd() };
        for (var i = startIndex + 1; i < lines.Length && snippet.Count < 8; i++)
        {
            var previous = lines[i - 1].TrimEnd();
            var currentRaw = lines[i];
            var currentTrim = currentRaw.Trim();
            if (string.IsNullOrWhiteSpace(currentTrim))
                break;

            var continuation = previous.EndsWith("`", StringComparison.Ordinal);
            var indentedSwitch = (currentRaw.StartsWith(" ", StringComparison.Ordinal) || currentRaw.StartsWith("\t", StringComparison.Ordinal)) &&
                                 (currentTrim.StartsWith("-", StringComparison.Ordinal) || currentTrim.StartsWith("|", StringComparison.Ordinal));

            if (!continuation && !indentedSwitch)
                break;

            snippet.Add(currentRaw.TrimEnd());
        }

        return string.Join(Environment.NewLine, snippet).Trim();
    }

    private static string? BuildGeneratedPowerShellExample(ApiTypeModel type)
    {
        if (type is null || string.IsNullOrWhiteSpace(type.Name))
            return null;

        var method = type.Methods
            .OrderByDescending(static m => m.Parameters.Count(static p => !p.IsOptional))
            .ThenByDescending(static m => m.Parameters.Count)
            .FirstOrDefault();

        var parameters = method?.Parameters ?? new List<ApiParameterModel>();
        var picked = parameters
            .Where(static p => !p.IsOptional)
            .Take(4)
            .ToList();

        if (picked.Count == 0)
        {
            var candidate =
                parameters.FirstOrDefault(p => p.Name.Equals("Path", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault(p => p.Name.EndsWith("Path", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault(p => p.Name.Equals("ModuleName", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault(p => p.Name.Equals("Name", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault();

            if (candidate is not null)
                picked.Add(candidate);
        }

        var parts = new List<string> { type.Name };
        foreach (var parameter in picked)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
                continue;

            parts.Add("-" + parameter.Name);
            if (IsPowerShellSwitchParameter(parameter.Type))
                continue;
            parts.Add(GetPowerShellSampleValue(parameter.Name, parameter.Type));
        }

        return string.Join(" ", parts.Where(static p => !string.IsNullOrWhiteSpace(p)));
    }

    private static bool IsPowerShellSwitchParameter(string? typeName)
        => !string.IsNullOrWhiteSpace(typeName) &&
           typeName.Equals("SwitchParameter", StringComparison.OrdinalIgnoreCase);

    private static string GetPowerShellSampleValue(string parameterName, string? typeName)
    {
        var name = parameterName?.Trim() ?? string.Empty;
        var type = typeName?.Trim() ?? string.Empty;

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            var inner = type[..^2];
            return $"@({GetPowerShellSampleValue(name, inner)})";
        }

        if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) || type.Equals("Bool", StringComparison.OrdinalIgnoreCase))
            return "$true";
        if (type.Equals("Int32", StringComparison.OrdinalIgnoreCase) || type.Equals("Int64", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("UInt32", StringComparison.OrdinalIgnoreCase) || type.Equals("UInt64", StringComparison.OrdinalIgnoreCase))
            return "1";
        if (type.Equals("Double", StringComparison.OrdinalIgnoreCase) || type.Equals("Single", StringComparison.OrdinalIgnoreCase) || type.Equals("Decimal", StringComparison.OrdinalIgnoreCase))
            return "1";
        if (type.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
            return "'2000-01-01'";
        if (type.Equals("ScriptBlock", StringComparison.OrdinalIgnoreCase))
            return "{ }";
        if (type.Equals("Hashtable", StringComparison.OrdinalIgnoreCase) || type.Equals("IDictionary", StringComparison.OrdinalIgnoreCase))
            return "@{}";

        if (name.Equals("Path", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Path", StringComparison.OrdinalIgnoreCase))
            return "'C:\\Path'";
        if (name.EndsWith("Name", StringComparison.OrdinalIgnoreCase))
            return "'Name'";
        if (name.EndsWith("Uri", StringComparison.OrdinalIgnoreCase))
            return "'https://example.com'";

        return "'Value'";
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path, string pattern, SearchOption searchOption)
    {
        try
        {
            return Directory.EnumerateDirectories(path, pattern, searchOption);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern, SearchOption searchOption)
    {
        try
        {
            return Directory.EnumerateFiles(path, pattern, searchOption);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
