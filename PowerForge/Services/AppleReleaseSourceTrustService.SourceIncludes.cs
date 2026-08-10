using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static readonly HashSet<string> SourceIncludeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cc", ".cpp", ".cxx", ".m", ".mm", ".h", ".hh", ".hpp", ".hxx", ".inc", ".pch"
    };

    private void ValidateSourceLevelIncludes(string repositoryRoot, string sourcePath)
    {
        if (!SourceIncludeExtensions.Contains(Path.GetExtension(sourcePath)))
            return;
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!_validatedSourceIncludeFiles.Add(fullSourcePath))
            return;

        var source = RemoveCComments(File.ReadAllText(fullSourcePath));
        foreach (Match directive in Regex.Matches(
                     source,
                     "(?m)^[ \\t]*#[ \\t]*(?:include|include_next|import)[ \\t]+(?<operand>[^\\r\\n]+)",
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
