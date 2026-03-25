using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private sealed record GeneratedPowerShellExample(string Label, string Code);
    private sealed record ImportedPowerShellExampleCandidate(string Command, string Snippet, string FilePath, int Score, int LineNumber);

    private static readonly Regex PowerShellCommandTokenRegex = new(
        @"\b[A-Za-z][A-Za-z0-9]*-[A-Za-z0-9][A-Za-z0-9_.-]*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex PowerShellCultureFolderRegex = new(
        @"^[a-z]{2,3}(?:-[a-z0-9]{2,8})*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
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
                        Text = snippet,
                        Origin = ApiExampleOrigins.ImportedScript
                    });
                }
                continue;
            }

            foreach (var fallback in BuildGeneratedPowerShellExamples(type, limit))
            {
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "text",
                    Text = fallback.Label,
                    Origin = ApiExampleOrigins.GeneratedFallback
                });
                type.Examples.Add(new ApiExampleModel
                {
                    Kind = "code",
                    Text = fallback.Code,
                    Origin = ApiExampleOrigins.GeneratedFallback
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
            if (parent is not null &&
                parent.Exists &&
                ShouldProbePowerShellHelpParentDirectory(helpDir))
            {
                roots.Add(parent.FullName);
            }
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

    private static bool ShouldProbePowerShellHelpParentDirectory(string helpDirectory)
    {
        if (string.IsNullOrWhiteSpace(helpDirectory))
            return false;

        var folderName = Path.GetFileName(helpDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName))
            return false;

        return folderName.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               folderName.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
               folderName.Equals("reference", StringComparison.OrdinalIgnoreCase) ||
               PowerShellCultureFolderRegex.IsMatch(folderName);
    }

    private static Dictionary<string, List<string>> CollectPowerShellExamplesFromScripts(
        IReadOnlyList<string> files,
        IReadOnlyList<string> commandNames,
        int maxPerCommand,
        List<string> warnings)
    {
        var results = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var dedupe = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, List<ImportedPowerShellExampleCandidate>>(StringComparer.OrdinalIgnoreCase);
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
                foreach (var command in EnumeratePowerShellCommandTokensFromLine(line))
                {
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

                    if (!candidates.TryGetValue(command, out var commandCandidates))
                    {
                        commandCandidates = new List<ImportedPowerShellExampleCandidate>();
                        candidates[command] = commandCandidates;
                    }

                    commandCandidates.Add(new ImportedPowerShellExampleCandidate(
                        command,
                        snippet,
                        file,
                        GetImportedPowerShellExampleScore(command, file, snippet, i),
                        i));
                }
            }
        }

        foreach (var pair in candidates)
        {
            results[pair.Key] = pair.Value
                .OrderByDescending(static candidate => candidate.Score)
                .ThenBy(static candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static candidate => candidate.LineNumber)
                .Take(Math.Max(1, maxPerCommand))
                .Select(static candidate => candidate.Snippet)
                .ToList();
        }

        return results;
    }

    private static string[] CollectPowerShellCommandTokensFromFile(string filePath, List<string>? warnings)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Array.Empty<string>();

        try
        {
            return CollectPowerShellCommandTokens(File.ReadAllLines(filePath));
        }
        catch (Exception ex)
        {
            warnings?.Add($"API docs PowerShell coverage: skipped PowerShell examples file '{Path.GetFileName(filePath)}' ({ex.GetType().Name}: {ex.Message})");
            return Array.Empty<string>();
        }
    }

    private static string[] CollectPowerShellCommandTokens(IEnumerable<string> lines)
    {
        if (lines is null)
            return Array.Empty<string>();

        return lines
            .SelectMany(EnumeratePowerShellCommandTokensFromLine)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumeratePowerShellCommandTokensFromLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            yield break;

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
            yield break;

        foreach (Match match in PowerShellCommandTokenRegex.Matches(line))
        {
            if (!string.IsNullOrWhiteSpace(match.Value))
                yield return match.Value;
        }
    }

    private static int GetImportedPowerShellExampleScore(string commandName, string filePath, string snippet, int lineNumber)
    {
        var score = 0;
        var normalizedCommand = NormalizePowerShellExampleToken(commandName);
        var commandNoun = GetPowerShellCommandNoun(commandName);
        var normalizedNoun = NormalizePowerShellExampleToken(commandNoun);
        var fileName = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
        var normalizedFileName = NormalizePowerShellExampleToken(fileName);
        var normalizedPath = NormalizePowerShellExampleToken(filePath.Replace('\\', '/'));

        if (!string.IsNullOrWhiteSpace(normalizedCommand))
        {
            if (string.Equals(normalizedFileName, normalizedCommand, StringComparison.Ordinal))
                score += 140;
            else if (normalizedFileName.Contains(normalizedCommand, StringComparison.Ordinal))
                score += 100;

            if (normalizedPath.Contains(normalizedCommand, StringComparison.Ordinal))
                score += 35;
        }

        if (!string.IsNullOrWhiteSpace(normalizedNoun))
        {
            if (string.Equals(normalizedFileName, normalizedNoun, StringComparison.Ordinal))
                score += 70;
            else if (normalizedFileName.Contains(normalizedNoun, StringComparison.Ordinal))
                score += 35;
        }

        if (!string.IsNullOrWhiteSpace(snippet))
        {
            var firstLine = snippet
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line))
                ?.Trim() ?? string.Empty;
            if (firstLine.StartsWith(commandName + " ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(firstLine, commandName, StringComparison.OrdinalIgnoreCase))
                score += 18;
        }

        score -= Math.Max(0, lineNumber);
        return score;
    }

    private static string NormalizePowerShellExampleToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value
            .Where(static ch => char.IsLetterOrDigit(ch))
            .Select(static ch => char.ToLowerInvariant(ch))
            .ToArray();
        return new string(chars);
    }

    private static string GetPowerShellCommandNoun(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return string.Empty;

        var dash = commandName.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0 || dash + 1 >= commandName.Length)
            return commandName;

        return commandName[(dash + 1)..];
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

    private static IReadOnlyList<GeneratedPowerShellExample> BuildGeneratedPowerShellExamples(ApiTypeModel type, int limit)
    {
        if (type is null || string.IsNullOrWhiteSpace(type.Name) || limit <= 0)
            return Array.Empty<GeneratedPowerShellExample>();

        var examples = new List<GeneratedPowerShellExample>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var methods = type.Methods
            .Where(static method => method is not null)
            .OrderByDescending(GetGeneratedPowerShellExampleScore)
            .ThenBy(static method => method.Parameters.Count(static p => !p.IsOptional))
            .ThenBy(static method => method.Parameters.Count)
            .ThenBy(static method => method.ParameterSetName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var method in methods)
        {
            var code = BuildGeneratedPowerShellExample(type.Name, method);
            if (string.IsNullOrWhiteSpace(code) || !seen.Add(code))
                continue;

            examples.Add(new GeneratedPowerShellExample(BuildGeneratedPowerShellExampleLabel(method), code));
            if (examples.Count >= limit)
                break;
        }

        return examples;
    }

    private static string? BuildGeneratedPowerShellExample(string commandName, ApiMemberModel? method)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return null;

        var parameters = method?.Parameters ?? new List<ApiParameterModel>();
        var picked = parameters
            .Where(static p => !p.IsOptional)
            .Take(4)
            .ToList();

        if (picked.Count == 0)
        {
            var candidate = parameters
                .OrderByDescending(GetGeneratedPowerShellExampleParameterScore)
                .ThenBy(static p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (candidate is not null)
                picked.Add(candidate);
        }

        var parts = new List<string> { commandName };
        foreach (var parameter in picked)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
                continue;

            parts.Add("-" + parameter.Name);
            if (IsPowerShellSwitchParameter(parameter.Type))
                continue;
            parts.Add(GetPowerShellSampleValue(parameter));
        }

        return string.Join(" ", parts.Where(static p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string BuildGeneratedPowerShellExampleLabel(ApiMemberModel method)
    {
        if (!string.IsNullOrWhiteSpace(method?.ParameterSetName))
            return $"Generated fallback example from parameter set '{method.ParameterSetName}'.";
        return "Generated fallback example from command syntax.";
    }

    private static int GetGeneratedPowerShellExampleScore(ApiMemberModel method)
    {
        if (method is null)
            return int.MinValue;

        var required = method.Parameters.Where(static p => !p.IsOptional).ToList();
        var optional = method.Parameters.Where(static p => p.IsOptional).ToList();
        var score = 0;

        if (required.Count == 0)
        {
            score += 14;
        }
        else
        {
            score += Math.Max(0, 36 - Math.Abs(required.Count - 1) * 10);
        }

        if (required.Count <= 3)
            score += 8;
        if (required.Count > 4)
            score -= (required.Count - 4) * 8;

        score += required.Sum(GetGeneratedPowerShellExampleParameterScore);
        score += optional
            .Select(GetGeneratedPowerShellExampleParameterScore)
            .DefaultIfEmpty(0)
            .Max() / 3;

        return score;
    }

    private static int GetGeneratedPowerShellExampleParameterScore(ApiParameterModel? parameter)
    {
        if (parameter is null)
            return int.MinValue;

        var score = 0;
        var name = parameter.Name?.Trim() ?? string.Empty;
        var type = parameter.Type?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(name))
        {
            if (name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Mode", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Uri", StringComparison.OrdinalIgnoreCase))
                score += 30;
            else if (name.EndsWith("Name", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith("Path", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith("Uri", StringComparison.OrdinalIgnoreCase))
                score += 22;

            if (name.Equals("InputObject", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Credential", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Session", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("CimSession", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PSSession", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ScriptBlock", StringComparison.OrdinalIgnoreCase))
                score -= 28;

            if (name.Equals("WhatIf", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Confirm", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Verbose", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ErrorAction", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("WarningAction", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("InformationAction", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ProgressAction", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("OutVariable", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("OutBuffer", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PipelineVariable", StringComparison.OrdinalIgnoreCase))
                score -= 40;
        }

        if (IsPowerShellFriendlyExampleType(type))
            score += 12;
        else if (!string.IsNullOrWhiteSpace(type))
            score -= 16;

        if (parameter.PossibleValues.Count > 0)
            score += 8;
        if (IsPowerShellSwitchParameter(type))
            score -= 6;

        return score;
    }

    private static bool IsPowerShellFriendlyExampleType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return true;

        var type = typeName.Trim();
        if (type.EndsWith("[]", StringComparison.Ordinal))
            return IsPowerShellFriendlyExampleType(type[..^2]);

        return type.Equals("String", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("string", StringComparison.OrdinalIgnoreCase) ||
               type.EndsWith(".String", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Int32", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("int", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Int64", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("long", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("UInt32", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("uint", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("UInt64", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("ulong", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Int16", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("short", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("UInt16", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("ushort", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Byte", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("byte", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("SByte", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("sbyte", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Double", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("double", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Single", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("float", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Decimal", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Bool", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("bool", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Guid", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("guid", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Uri", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("uri", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("DateTime", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("datetime", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("Hashtable", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("IDictionary", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("ScriptBlock", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("SwitchParameter", StringComparison.OrdinalIgnoreCase) ||
               type.EndsWith(".SwitchParameter", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerShellSwitchParameter(string? typeName)
        => !string.IsNullOrWhiteSpace(typeName) &&
           (typeName.Equals("SwitchParameter", StringComparison.OrdinalIgnoreCase) ||
            (typeName.EndsWith(".SwitchParameter", StringComparison.OrdinalIgnoreCase) &&
             typeName.Length > ".SwitchParameter".Length));

    private static string GetPowerShellSampleValue(ApiParameterModel parameter)
    {
        if (parameter is not null && parameter.PossibleValues.Count > 0)
        {
            var possibleValue = parameter.PossibleValues
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(possibleValue))
            {
                if (bool.TryParse(possibleValue, out var boolValue))
                    return boolValue ? "$true" : "$false";

                if (int.TryParse(possibleValue, out _))
                    return possibleValue;

                var escaped = possibleValue.Replace("'", "''", StringComparison.Ordinal);
                return $"'{escaped}'";
            }
        }

        return GetPowerShellSampleValue(parameter?.Name ?? string.Empty, parameter?.Type);
    }

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
            type.Equals("UInt32", StringComparison.OrdinalIgnoreCase) || type.Equals("UInt64", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Int16", StringComparison.OrdinalIgnoreCase) || type.Equals("UInt16", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Byte", StringComparison.OrdinalIgnoreCase) || type.Equals("SByte", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("int", StringComparison.OrdinalIgnoreCase) || type.Equals("long", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("uint", StringComparison.OrdinalIgnoreCase) || type.Equals("ulong", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("short", StringComparison.OrdinalIgnoreCase) || type.Equals("ushort", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("byte", StringComparison.OrdinalIgnoreCase) || type.Equals("sbyte", StringComparison.OrdinalIgnoreCase))
            return "1";
        if (type.Equals("Double", StringComparison.OrdinalIgnoreCase) || type.Equals("Single", StringComparison.OrdinalIgnoreCase) || type.Equals("Decimal", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("double", StringComparison.OrdinalIgnoreCase) || type.Equals("float", StringComparison.OrdinalIgnoreCase) || type.Equals("decimal", StringComparison.OrdinalIgnoreCase))
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
            return Directory.GetDirectories(path, pattern, searchOption);
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
            return Directory.GetFiles(path, pattern, searchOption);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
