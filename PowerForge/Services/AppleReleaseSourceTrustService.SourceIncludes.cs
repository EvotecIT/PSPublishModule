using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static readonly HashSet<string> SourceIncludeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cc", ".cpp", ".cxx", ".m", ".mm", ".metal", ".s",
        ".h", ".hh", ".hpp", ".hxx", ".inc", ".pch", ".modulemap", ".swift"
    };

    private void ValidateSourceLevelIncludes(
        string repositoryRoot,
        string sourcePath,
        bool validateSwiftDeterminism = false)
    {
        if (!SourceIncludeExtensions.Contains(Path.GetExtension(sourcePath)))
            return;
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(fullSourcePath);
        if (extension.Equals(".swift", StringComparison.OrdinalIgnoreCase) && !validateSwiftDeterminism)
            return;
        if (!_validatedSourceIncludeFiles.Add(fullSourcePath))
            return;

        if (extension.Equals(".modulemap", StringComparison.OrdinalIgnoreCase))
        {
            ValidateClangModuleMapInputs(repositoryRoot, fullSourcePath);
            return;
        }
        if (extension.Equals(".swift", StringComparison.OrdinalIgnoreCase))
        {
            ValidateSwiftSourceDeterminism(fullSourcePath);
            return;
        }

        // C and Objective-C splice escaped physical lines before comments and
        // preprocessing directives are interpreted. Scan that logical source so
        // an include keyword cannot be split across lines to evade attestation.
        var source = RemoveCComments(SpliceCPreprocessingLines(File.ReadAllText(fullSourcePath)));
        var nondeterministicMacro = FindNondeterministicCompilerMacro(source);
        if (nondeterministicMacro is not null)
        {
            throw new InvalidOperationException(
                $"Source input '{fullSourcePath}' uses nondeterministic compiler macro '{nondeterministicMacro}', which cannot be bound to one reproducible source commit.");
        }
        foreach (Match directive in Regex.Matches(
                     source,
                     "(?m)^[ \\t]*(?:#|%:)[ \\t]*(?:include|include_next|import)[ \\t]+(?<operand>[^\\r\\n]+)",
                     RegexOptions.CultureInvariant))
        {
            var operand = Regex.Replace(directive.Groups["operand"].Value, "[ \\t]*(?://.*)?$", string.Empty).Trim();
            var quoted = operand.Length >= 2 && operand[0] == '"' && operand[operand.Length - 1] == '"';
            var angled = operand.Length >= 2 && operand[0] == '<' && operand[operand.Length - 1] == '>';
            if (!quoted && !angled)
            {
                throw new InvalidOperationException(
                    $"Source input '{fullSourcePath}' uses computed preprocessor include '{operand}', which cannot be bound to the exact source commit.");
            }

            var include = operand.Substring(1, operand.Length - 2).Trim();
            if (Path.IsPathRooted(include))
            {
                throw new InvalidOperationException(
                    $"Source input '{fullSourcePath}' references absolute preprocessor include '{include}', which is outside the exact-source graph.");
            }

            var segments = include.Split('/', '\\');
            if (angled)
            {
                if (segments.Any(static segment => segment == ".."))
                    throw new InvalidOperationException($"Source input '{fullSourcePath}' uses escaping system include '{include}'.");
                continue;
            }

            var candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullSourcePath)!, include));
            EnsurePathWithinRepository(repositoryRoot, candidate, $"preprocessor include from {fullSourcePath}");
            if (File.Exists(candidate))
                EnsureTrackedFile(repositoryRoot, candidate, $"preprocessor include from {fullSourcePath}");
        }
    }

    private static void ValidateSwiftSourceDeterminism(string sourcePath)
    {
        var contents = File.ReadAllText(sourcePath);
        if (contents.IndexOf("#file", StringComparison.Ordinal) < 0)
            return;
        var syntax = MaskSwiftStringLiterals(RemoveSwiftComments(contents));
        var locationLiteral = Regex.Match(
            syntax,
            "(?<![A-Za-z0-9_])#(?<literal>file|filePath)(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
        if (!locationLiteral.Success)
            return;
        throw new InvalidOperationException(
            $"Swift source input '{sourcePath}' uses snapshot-path compiler literal '#{locationLiteral.Groups["literal"].Value}', " +
            "which exposes changing checkout or host state and cannot be bound to one reproducible detached source location. " +
            "Use #fileID or an explicit stable identifier instead.");
    }

    private void ValidateClangModuleMapInputs(string repositoryRoot, string moduleMapPath)
    {
        var source = RemoveCComments(File.ReadAllText(moduleMapPath));
        const string declaration =
            "(?<![A-Za-z0-9_])(?:" +
            "(?:(?:private|textual)\\s+)*header|" +
            "umbrella(?:\\s+header)?|" +
            "exclude\\s+header|" +
            "extern\\s+module\\s+[A-Za-z_][A-Za-z0-9_.]*" +
            ")\\s*\"(?<path>(?:\\\\.|[^\"\\\\])*)\"";
        foreach (Match match in Regex.Matches(
                     source,
                     declaration,
                     RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            var declaredPath = match.Groups["path"].Value;
            if (declaredPath.Contains('\\'))
            {
                throw new InvalidOperationException(
                    $"Clang module map '{moduleMapPath}' uses an escaped or platform-dependent input path '{declaredPath}', which cannot be attested safely.");
            }
            if (Path.IsPathRooted(declaredPath))
            {
                throw new InvalidOperationException(
                    $"Clang module map '{moduleMapPath}' references absolute input '{declaredPath}', which is outside the exact-source graph.");
            }

            var candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(moduleMapPath)!, declaredPath));
            EnsurePathWithinRepository(repositoryRoot, candidate, $"Clang module map input from {moduleMapPath}");
            if (File.Exists(candidate))
            {
                EnsureTrackedFile(repositoryRoot, candidate, $"Clang module map input from {moduleMapPath}");
            }
            else if (Directory.Exists(candidate))
            {
                EnsureTrackedDirectoryTree(repositoryRoot, candidate, $"Clang module map input from {moduleMapPath}");
            }
            else
            {
                throw new FileNotFoundException(
                    $"Clang module map input was not found inside the exact checked-out source: {candidate}",
                    candidate);
            }
        }
    }

    private static string? FindNondeterministicCompilerMacro(string source)
    {
        var masked = MaskCStringAndCharacterLiterals(source);
        var found = FindNondeterministicCompilerIdentifier(masked);
        if (found is not null)
            return found;

        var tokenPasted = Regex.Replace(masked, "[ \\t\\r\\n]*(?:##|%:%:)[ \\t\\r\\n]*", string.Empty);
        return FindNondeterministicCompilerIdentifier(tokenPasted);
    }

    private static string? FindNondeterministicCompilerIdentifier(string source)
    {
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '_' && !char.IsLetter(source[index]))
                continue;
            var start = index;
            while (index + 1 < source.Length &&
                   (source[index + 1] == '_' || char.IsLetterOrDigit(source[index + 1])))
                index++;
            var identifier = source.Substring(start, index - start + 1);
            if (identifier is "__DATE__" or "__TIME__" or "__TIMESTAMP__" or
                "__FILE__" or "__BASE_FILE__" or "__builtin_FILE" or
                "__builtin_source_location" or "source_location")
                return identifier;
        }
        return null;
    }

    private static string MaskCStringAndCharacterLiterals(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var quote = '\0';
        var escaped = false;
        foreach (var current in source)
        {
            if (quote == '\0')
            {
                if (current is '\"' or '\'')
                {
                    quote = current;
                    result.Append(' ');
                }
                else
                {
                    result.Append(current);
                }
                continue;
            }

            result.Append(current is '\r' or '\n' ? current : ' ');
            if (escaped)
                escaped = false;
            else if (current == '\\')
                escaped = true;
            else if (current == quote)
                quote = '\0';
        }
        return result.ToString();
    }

    private static string SpliceCPreprocessingLines(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '\\' || index + 1 >= source.Length)
            {
                result.Append(source[index]);
                continue;
            }

            if (source[index + 1] == '\n')
            {
                index++;
                continue;
            }
            if (source[index + 1] == '\r')
            {
                index++;
                if (index + 1 < source.Length && source[index + 1] == '\n')
                    index++;
                continue;
            }

            result.Append(source[index]);
        }
        return result.ToString();
    }

    private static string RemoveCComments(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var inBlockComment = false;
        var inLineComment = false;
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (inLineComment)
            {
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                    result.Append(current);
                }
                else
                {
                    result.Append(' ');
                }
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    result.Append("  ");
                    index++;
                    inBlockComment = false;
                }
                else
                {
                    result.Append(current == '\r' || current == '\n' ? current : ' ');
                }
                continue;
            }
            if (quote != '\0')
            {
                result.Append(current);
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == quote)
                    quote = '\0';
                continue;
            }
            if (current == '/' && next == '/')
            {
                result.Append("  ");
                index++;
                inLineComment = true;
                continue;
            }
            if (current == '/' && next == '*')
            {
                result.Append("  ");
                index++;
                inBlockComment = true;
                continue;
            }
            if (current == '"' || current == '\'')
                quote = current;
            result.Append(current);
        }
        return result.ToString();
    }
}
