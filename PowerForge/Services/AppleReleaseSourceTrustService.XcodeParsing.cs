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
                values = ExtractBuildFlagInputPaths(assignment.Value, key);
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
                if (string.IsNullOrWhiteSpace(value) || IsToolchainOrBuildProductPath(value))
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
                     "(?m)^[ \\t]*(?!#)(?<key>[A-Za-z_][A-Za-z0-9_]*(?:\\[[^\\]]+\\])?)[ \\t]*(?:\\?=|\\+=|=)[ \\t]*(?<value>.*?)[ \\t]*(?://.*)?$",
                     RegexOptions.CultureInvariant))
        {
            yield return new KeyValuePair<string, string>(
                match.Groups["key"].Value,
                match.Groups["value"].Value.Trim());
        }
    }

    private static IEnumerable<string> ExtractBuildFlagInputPaths(string value, string key)
    {
        var tokens = ExpandForwardedBuildFlagTokens(SplitBuildSettingPaths(value), key);
        var consumeNext = false;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (consumeNext)
            {
                consumeNext = false;
                yield return token;
                continue;
            }

            if (token.Equals("-I", StringComparison.Ordinal) ||
                token.Equals("-F", StringComparison.Ordinal) ||
                token.Equals("-L", StringComparison.Ordinal) ||
                token.Equals("-include", StringComparison.Ordinal) ||
                token.Equals("-force_load", StringComparison.Ordinal) ||
                token.Equals("-filelist", StringComparison.Ordinal) ||
                token.Equals("-ivfsoverlay", StringComparison.Ordinal) ||
                token.Equals("-vfsoverlay", StringComparison.Ordinal) ||
                token.Equals("-fplugin", StringComparison.Ordinal) ||
                token.Equals("-fpass-plugin", StringComparison.Ordinal) ||
                token.Equals("-load", StringComparison.Ordinal) ||
                token.Equals("-plugin", StringComparison.Ordinal) ||
                token.Equals("-plugin-path", StringComparison.Ordinal) ||
                token.Equals("-module-map-file", StringComparison.Ordinal) ||
                token.Equals("-isysroot", StringComparison.Ordinal) ||
                token.Equals("-sdk", StringComparison.Ordinal))
            {
                consumeNext = true;
                continue;
            }

            var prefixes = new[]
            {
                "-I", "-F", "-L", "-fmodule-map-file=", "-fplugin=", "-fpass-plugin=",
                "-ivfsoverlay=", "-vfsoverlay=", "-plugin-path=", "-module-map-file=",
                "-isysroot=", "-sdk="
            };
            var prefix = prefixes.FirstOrDefault(candidate =>
                token.StartsWith(candidate, StringComparison.Ordinal) && token.Length > candidate.Length);
            if (prefix is not null)
            {
                yield return token.Substring(prefix.Length);
                continue;
            }

            if (token.StartsWith("-D", StringComparison.Ordinal) ||
                token.StartsWith("-U", StringComparison.Ordinal) ||
                token.StartsWith("-Werror=", StringComparison.Ordinal) ||
                token.StartsWith("-Wno-", StringComparison.Ordinal) ||
                token.Contains("@executable_path", StringComparison.Ordinal) ||
                token.Contains("@loader_path", StringComparison.Ordinal) ||
                token.Contains("@rpath", StringComparison.Ordinal))
            {
                continue;
            }

            if (token.StartsWith("-", StringComparison.Ordinal) && IsPathLikeBuildFlagToken(token))
            {
                throw new InvalidOperationException(
                    $"Path-bearing option in Xcode build setting {key} cannot be classified safely: {token}");
            }

            if (!token.StartsWith("-", StringComparison.Ordinal) &&
                IsPathLikeBuildFlagToken(token))
            {
                throw new InvalidOperationException(
                    $"Path-like token in Xcode build setting {key} cannot be classified safely: {token}");
            }
        }
        if (consumeNext)
            throw new InvalidOperationException($"Xcode build setting {key} ends with a path-consuming flag and no input.");
    }

    private static string[] ExpandForwardedBuildFlagTokens(string[] tokens, string key)
    {
        var expanded = new List<string>(tokens.Length);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Equals("-Xcc", StringComparison.Ordinal) ||
                token.Equals("-Xlinker", StringComparison.Ordinal) ||
                token.Equals("-Xfrontend", StringComparison.Ordinal) ||
                token.Equals("-Xswiftc", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with forwarding option '{token}' and no argument.");
                expanded.Add(tokens[index]);
                continue;
            }

            if (token.StartsWith("-Wl,", StringComparison.Ordinal))
            {
                expanded.AddRange(token.Substring(4).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                continue;
            }

            foreach (var wrapper in new[] { "-Xcc=", "-Xlinker=", "-Xfrontend=", "-Xswiftc=" })
            {
                if (!token.StartsWith(wrapper, StringComparison.Ordinal))
                    continue;
                expanded.Add(token.Substring(wrapper.Length));
                token = string.Empty;
                break;
            }
            if (!string.IsNullOrEmpty(token))
                expanded.Add(token);
        }
        return expanded.ToArray();
    }

    private static bool IsPathLikeBuildFlagToken(string token)
        => Path.IsPathRooted(token) ||
           token.Contains('/') ||
           token.Contains('\\') ||
           token.Contains("$(", StringComparison.Ordinal) ||
           token.Contains("${", StringComparison.Ordinal);

    private static bool IsToolchainOrBuildProductPath(string value)
    {
        var known = new[]
        {
            "$(SDKROOT)", "${SDKROOT}",
            "$(DEVELOPER_DIR)", "${DEVELOPER_DIR}",
            "$(TOOLCHAIN_DIR)", "${TOOLCHAIN_DIR}",
            "$(BUILT_PRODUCTS_DIR)", "${BUILT_PRODUCTS_DIR}",
            "$(CONFIGURATION_BUILD_DIR)", "${CONFIGURATION_BUILD_DIR}",
            "$(TARGET_BUILD_DIR)", "${TARGET_BUILD_DIR}"
        };
        return known.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, PbxObject> ParsePbxObjects(string text)
    {
        var objects = new Dictionary<string, PbxObject>(StringComparer.OrdinalIgnoreCase);
        foreach (Match start in Regex.Matches(
                     text,
                     "(?m)^[ \\t]*(?<id>[A-Fa-f0-9]{8,32})(?:[ \\t]+/\\*.*?\\*/)?[ \\t]*=[ \\t]*\\{",
                     RegexOptions.CultureInvariant))
        {
            var id = start.Groups["id"].Value;
            var openingBrace = text.IndexOf('{', start.Index + start.Length - 1);
            var closingBrace = FindMatchingPbxBrace(text, openingBrace);
            var body = text.Substring(openingBrace + 1, closingBrace - openingBrace - 1);
            var isa = ReadPbxScalar(body, "isa");
            if (string.IsNullOrWhiteSpace(isa))
                continue;
            objects[id] = new PbxObject
            {
                Id = id,
                Isa = isa!,
                Path = ReadPbxScalar(body, "path"),
                SourceTree = ReadPbxScalar(body, "sourceTree"),
                Body = body
            };
        }
        return objects;
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
            "(?:^|[\\r\\n;])[ \\t]*" + Regex.Escape(name) + "[ \\t]*=[ \\t]*\\{",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var openingBrace = body.IndexOf('{', match.Index + match.Length - 1);
        var closingBrace = FindMatchingPbxBrace(body, openingBrace);
        return body.Substring(openingBrace + 1, closingBrace - openingBrace - 1);
    }

    private static IEnumerable<KeyValuePair<string, string>> ReadPbxAssignments(string body)
    {
        foreach (Match match in Regex.Matches(
                     body,
                     "(?m)^[ \\t]*(?:\\\"(?<quotedKey>(?:\\\\.|[^\\\"])*)\\\"|(?<bareKey>[^=\\r\\n]+?))[ \\t]*=[ \\t]*(?<value>\\([^;]*?\\)|\\\"(?:\\\\.|[^\\\"])*\\\"|[^;\\r\\n]+)[ \\t]*;",
                     RegexOptions.CultureInvariant))
        {
            var key = match.Groups["quotedKey"].Success
                ? UnescapePbxString(match.Groups["quotedKey"].Value)
                : match.Groups["bareKey"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
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
        return Regex.Matches(match.Groups["items"].Value, "(?m)^[ \\t]*(?<id>[A-Fa-f0-9]{8,32})", RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(value => value.Groups["id"].Value)
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
