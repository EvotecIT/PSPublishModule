using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private void ValidateBuildSettingAssignments(
        string repositoryRoot,
        string projectDirectory,
        IEnumerable<KeyValuePair<string, string>> assignments,
        IReadOnlyCollection<string> generatedOutputPaths,
        string source)
    {
        foreach (var assignment in assignments)
        {
            var key = assignment.Key.Trim();
            var baseKey = key.Split('[')[0].Trim();
            if (ExecutableBuildSettings.Contains(baseKey) &&
                !string.IsNullOrWhiteSpace(assignment.Value) &&
                !assignment.Value.Trim().Equals("$(inherited)", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Xcode build setting {key} overrides a compiler or build executable and cannot be proven at the exact source commit: {source}");
            }
            if (SdkSelectionBuildSettings.Contains(baseKey))
            {
                ValidateSdkSelectionBuildSetting(key, assignment.Value, source);
                continue;
            }
            IEnumerable<string> values;
            if (FileValuedBuildSettings.Contains(baseKey) ||
                SearchPathBuildSettings.Contains(baseKey) ||
                baseKey.StartsWith("SCRIPT_INPUT_FILE_", StringComparison.OrdinalIgnoreCase) ||
                baseKey.StartsWith("SCRIPT_INPUT_FILE_LIST_", StringComparison.OrdinalIgnoreCase))
            {
                values = SplitBuildSettingPaths(assignment.Value);
            }
            else if (FlagBuildSettings.Contains(baseKey))
            {
                ValidateBuildFlagInputPaths(
                    repositoryRoot,
                    projectDirectory,
                    assignment.Value,
                    key,
                    generatedOutputPaths,
                    source,
                    new HashSet<string>(GetPathComparer()));
                continue;
            }
            else
            {
                continue;
            }

            foreach (var rawValue in values)
            {
                var value = rawValue.Trim().TrimEnd('/');
                while (value.EndsWith("/**", StringComparison.Ordinal))
                    value = value.Substring(0, value.Length - 3).TrimEnd('/');
                if (string.IsNullOrWhiteSpace(value) ||
                    IsValidatedToolchainOrBuildProductPath(value, key, source))
                    continue;
                var candidate = ResolveBuildSettingPath(projectDirectory, value, key);
                EnsurePathWithinRepository(repositoryRoot, candidate, $"Xcode build setting {key} from {source}");
                EnsureNoGeneratedOutputOverlap(candidate, generatedOutputPaths, $"Xcode build setting {key}");
                if (File.Exists(candidate))
                    EnsureTrackedFile(repositoryRoot, candidate, $"Xcode build setting {key}");
                else if (Directory.Exists(candidate))
                    EnsureTrackedDirectoryTree(repositoryRoot, candidate, $"Xcode build setting {key}");
                else
                    throw new FileNotFoundException(
                        $"Xcode build setting {key} references a missing exact-source input: {candidate}",
                        candidate);
            }
        }
    }


    private static IEnumerable<KeyValuePair<string, string>> ReadXcconfigAssignments(string contents)
    {
        var logical = Regex.Replace(contents, "\\\\[ \\t]*\\r?\\n", " ");
        foreach (Match match in Regex.Matches(
                     logical,
                     "(?m)^[ \\t]*(?!#)(?<key>[A-Za-z_][A-Za-z0-9_]*(?:\\[[^\\]]+\\])*)[ \\t]*(?:\\?=|\\+=|=)[ \\t]*(?<value>.*?)[ \\t]*(?://.*)?$",
                     RegexOptions.CultureInvariant))
        {
            yield return new KeyValuePair<string, string>(
                match.Groups["key"].Value,
                match.Groups["value"].Value.Trim());
        }
    }

    private static void ValidateSdkSelectionBuildSetting(string key, string value, string source)
    {
        var selector = value.Trim();
        if (string.IsNullOrWhiteSpace(selector) ||
            selector.Equals("$(inherited)", StringComparison.OrdinalIgnoreCase))
            return;
        if (Regex.IsMatch(selector, "^[A-Za-z0-9._*-]+$", RegexOptions.CultureInvariant))
            return;

        throw new InvalidOperationException(
            $"Xcode build setting {key} selects a custom SDK path or expression that cannot be proven at the exact source commit: {source}");
    }


    private static Dictionary<string, PbxObject> ParsePbxObjects(string text)
    {
        var objects = new Dictionary<string, PbxObject>(StringComparer.OrdinalIgnoreCase);
        var syntax = RemovePbxComments(text);
        var objectDictionary = ReadRootPbxObjectDictionary(syntax);
        foreach (var assignment in ReadPbxAssignments(objectDictionary))
        {
            var objectValue = assignment.Value.Trim();
            if (objectValue.Length < 2 || objectValue[0] != '{' || objectValue[objectValue.Length - 1] != '}')
                continue;
            var id = ParsePbxObjectIdentifier(assignment.Key, "object dictionary key");
            var body = objectValue.Substring(1, objectValue.Length - 2);
            var isa = ReadPbxScalar(body, "isa");
            if (!string.IsNullOrWhiteSpace(isa))
            {
                if (objects.ContainsKey(id))
                    throw new InvalidOperationException($"Xcode project repeats PBX object identifier '{id}'.");
                objects.Add(id, new PbxObject
                {
                    Id = id,
                    Isa = isa!,
                    Path = ReadPbxScalar(body, "path"),
                    SourceTree = ReadPbxScalar(body, "sourceTree"),
                    Body = body
                });
            }
        }
        return objects;
    }

    private static string ReadRootPbxObjectDictionary(string syntax)
    {
        var rootStart = SkipPbxTrivia(syntax, 0);
        if (rootStart >= syntax.Length || syntax[rootStart] != '{')
            return syntax;

        var rootEnd = FindMatchingPbxBrace(syntax, rootStart);
        var rootBody = syntax.Substring(rootStart + 1, rootEnd - rootStart - 1);
        var objectAssignments = ReadPbxAssignments(rootBody)
            .Where(static assignment => assignment.Key.Equals("objects", StringComparison.Ordinal))
            .Select(static assignment => assignment.Value)
            .ToArray();
        if (objectAssignments.Length != 1 ||
            objectAssignments[0].Length < 2 ||
            objectAssignments[0][0] != '{' ||
            objectAssignments[0][objectAssignments[0].Length - 1] != '}')
        {
            throw new InvalidOperationException(
                "Xcode project root does not contain one unambiguous top-level objects dictionary.");
        }
        var objects = objectAssignments[0];
        return objects.Substring(1, objects.Length - 2);
    }

    /// <summary>
    /// Removes OpenStep comments while retaining string contents and character positions closely enough
    /// for the PBX scanners to parse only syntax that Xcode itself observes.
    /// </summary>
    private static string RemovePbxComments(string text)
    {
        var result = text.ToCharArray();
        var inString = false;
        var escaped = false;
        for (var index = 0; index < result.Length; index++)
        {
            var current = result[index];
            var next = index + 1 < result.Length ? result[index + 1] : '\0';
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == '"')
                    inString = false;
                continue;
            }
            if (current == '"')
            {
                inString = true;
                continue;
            }
            if (current == '/' && next == '*')
            {
                result[index] = result[index + 1] = ' ';
                index += 2;
                while (index < result.Length)
                {
                    if (index + 1 < result.Length && result[index] == '*' && result[index + 1] == '/')
                    {
                        result[index] = result[index + 1] = ' ';
                        index++;
                        break;
                    }
                    if (result[index] != '\r' && result[index] != '\n')
                        result[index] = ' ';
                    index++;
                }
                if (index >= result.Length)
                    throw new InvalidOperationException("Xcode project contains an unterminated PBX comment.");
                continue;
            }
            if (current == '/' && next == '/')
            {
                result[index] = result[index + 1] = ' ';
                index += 2;
                while (index < result.Length && result[index] != '\r' && result[index] != '\n')
                {
                    result[index] = ' ';
                    index++;
                }
            }
        }
        return new string(result);
    }

    private static bool IsHexCharacter(char value)
        => value is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';

    private static string ParsePbxObjectIdentifier(string value, string context)
    {
        var identifier = value.Trim();
        if (identifier.Length >= 2 && identifier[0] == '"' && identifier[identifier.Length - 1] == '"')
            identifier = UnescapePbxString(identifier.Substring(1, identifier.Length - 2));
        if (identifier.Length < 8 || identifier.Length > 32 || identifier.Any(character => !IsHexCharacter(character)))
            throw new InvalidOperationException($"Xcode project contains an invalid PBX {context}: '{value}'.");
        return identifier;
    }

    private static int SkipPbxTrivia(string text, int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]) || text[index] == ';' || text[index] == ',')
            {
                index++;
                continue;
            }
            if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '*')
            {
                var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0)
                    throw new InvalidOperationException("Xcode project contains an unterminated PBX comment.");
                index = end + 2;
                continue;
            }
            if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/')
            {
                var end = text.IndexOf('\n', index + 2);
                index = end < 0 ? text.Length : end + 1;
                continue;
            }
            break;
        }
        return index;
    }

    private static int FindMatchingPbxBrace(string text, int openingBrace)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        var inLineComment = false;
        var inBlockComment = false;
        for (var index = openingBrace; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (inLineComment)
            {
                if (current == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }
                continue;
            }
            if (inString)
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == '"') inString = false;
                continue;
            }
            if (current == '/' && next == '/')
            {
                inLineComment = true;
                index++;
            }
            else if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
            }
            else if (current == '"') inString = true;
            else if (current == '{') depth++;
            else if (current == '}' && --depth == 0) return index;
        }
        throw new InvalidOperationException("Xcode project contains an unterminated PBX object.");
    }

    private static string? ReadPbxScalar(string body, string name)
    {
        var match = Regex.Match(
            body,
            "(?:^|[\\r\\n;])[ \\t]*" + Regex.Escape(name) +
            "[ \\t]*=[ \\t]*(?:\\\"(?<quoted>(?:\\\\.|[^\\\"])*)\\\"|(?<bare>[^;\\r\\n]+))[ \\t]*;",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        return match.Groups["quoted"].Success
            ? UnescapePbxString(match.Groups["quoted"].Value)
            : match.Groups["bare"].Value.Trim();
    }

    private static string? ReadPbxDictionary(string body, string name)
    {
        var match = Regex.Match(
            body,
            "(?:^|[{\\r\\n;])[ \\t]*" + Regex.Escape(name) + "[ \\t]*=[ \\t]*\\{",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var openingBrace = body.IndexOf('{', match.Index + match.Length - 1);
        var closingBrace = FindMatchingPbxBrace(body, openingBrace);
        return body.Substring(openingBrace + 1, closingBrace - openingBrace - 1);
    }

    private static IEnumerable<KeyValuePair<string, string>> ReadPbxAssignments(string body)
    {
        for (var index = 0; index < body.Length; index++)
        {
            index = SkipPbxTrivia(body, index);
            if (index >= body.Length)
                yield break;

            var keyStart = index;
            var inString = false;
            var escaped = false;
            while (index < body.Length)
            {
                var current = body[index];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == '"') inString = false;
                }
                else if (current == '"') inString = true;
                else if (current == '=') break;
                else if (current == ';') break;
                index++;
            }
            if (index >= body.Length || body[index] != '=')
                continue;

            var key = body.Substring(keyStart, index - keyStart).Trim();
            if (key.Length >= 2 && key[0] == '"' && key[key.Length - 1] == '"')
                key = UnescapePbxString(key.Substring(1, key.Length - 2));
            var valueStart = ++index;
            var parentheses = 0;
            var braces = 0;
            inString = false;
            escaped = false;
            var inLineComment = false;
            var inBlockComment = false;
            while (index < body.Length)
            {
                var current = body[index];
                var next = index + 1 < body.Length ? body[index + 1] : '\0';
                if (inLineComment)
                {
                    if (current == '\n') inLineComment = false;
                }
                else if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        inBlockComment = false;
                        index++;
                    }
                }
                else if (inString)
                {
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == '"') inString = false;
                }
                else if (current == '/' && next == '/')
                {
                    inLineComment = true;
                    index++;
                }
                else if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    index++;
                }
                else if (current == '"') inString = true;
                else if (current == '(') parentheses++;
                else if (current == ')') parentheses--;
                else if (current == '{') braces++;
                else if (current == '}') braces--;
                else if (current == ';' && parentheses == 0 && braces == 0) break;
                index++;
            }
            if (index >= body.Length)
                throw new InvalidOperationException($"Xcode PBX assignment '{key}' is not terminated.");

            var value = body.Substring(valueStart, index - valueStart).Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = UnescapePbxString(value.Substring(1, value.Length - 2));
            yield return new KeyValuePair<string, string>(key, value);
        }
    }

    private static string[] ReadPbxReferences(string body, string name)
    {
        var match = Regex.Match(
            body,
            "(?:^|[\\r\\n;])[ \\t]*" + Regex.Escape(name) + "[ \\t]*=[ \\t]*\\((?<items>.*?)\\)[ \\t]*;",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
            return Array.Empty<string>();
        return match.Groups["items"].Value
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ParsePbxObjectIdentifier(value, $"reference in {name}"))
            .ToArray();
    }

    private static string UnescapePbxString(string value)
    {
        var builder = new StringBuilder(value.Length);
        var escaped = false;
        foreach (var character in value)
        {
            if (escaped)
            {
                builder.Append(character);
                escaped = false;
            }
            else if (character == '\\')
            {
                escaped = true;
            }
            else
            {
                builder.Append(character);
            }
        }
        if (escaped) builder.Append('\\');
        return builder.ToString();
    }

    private sealed class PbxObject
    {
        internal string Id { get; set; } = string.Empty;

        internal string Isa { get; set; } = string.Empty;

        internal string? Path { get; set; }

        internal string? SourceTree { get; set; }

        internal string Body { get; set; } = string.Empty;
    }
}
