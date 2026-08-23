using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool DoesEvaluatedProjectReferenceIdentityMatch(
        string itemSpec,
        string referencedPath,
        IReadOnlyDictionary<string, EvaluatedProjectItem[]> evaluatedItemLists,
        StringComparison comparison)
    {
        return IsMsBuildPropertyFunctionExpression(itemSpec) &&
               evaluatedItemLists.TryGetValue(
                   "ProjectReference",
                   out EvaluatedProjectItem[]? evaluatedReferences) &&
               evaluatedReferences.Any(reference => string.Equals(
                   reference.FullPath,
                   referencedPath,
                   comparison));
    }

    private static bool IsMsBuildPropertyFunctionExpression(string value)
        => value.IndexOf("$([", StringComparison.Ordinal) >= 0;

    private static bool TryMatchProjectReferenceGlob(
        string definingDirectory,
        string? itemSpec,
        string referencedPath,
        StringComparison comparison)
    {
        if (string.IsNullOrWhiteSpace(itemSpec) ||
            itemSpec!.IndexOfAny(new[] { '*', '?' }) < 0 ||
            itemSpec.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            itemSpec.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            itemSpec.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
            !TryUnescapeMsBuildLiteral(itemSpec, out string? unescapedItemSpec))
        {
            return false;
        }

        string fullPattern = Path.GetFullPath(Path.IsPathRooted(unescapedItemSpec!)
            ? unescapedItemSpec!
            : Path.Combine(definingDirectory, unescapedItemSpec!));
        string normalizedPattern = fullPattern.Replace('\\', '/');
        string normalizedPath = Path.GetFullPath(referencedPath).Replace('\\', '/');
        string expression = BuildProjectReferenceGlobExpression(normalizedPattern);
        RegexOptions options = RegexOptions.CultureInvariant;
        if (comparison == StringComparison.OrdinalIgnoreCase)
            options |= RegexOptions.IgnoreCase;
        return Regex.IsMatch(normalizedPath, expression, options, TimeSpan.FromSeconds(1));
    }

    private static string BuildProjectReferenceGlobExpression(string pattern)
    {
        var expression = new StringBuilder("^");
        for (int index = 0; index < pattern.Length; index++)
        {
            char character = pattern[index];
            if (character == '*')
            {
                bool recursive = index + 1 < pattern.Length && pattern[index + 1] == '*';
                bool followedBySeparator = recursive &&
                    index + 2 < pattern.Length &&
                    pattern[index + 2] == '/';
                expression.Append(followedBySeparator
                    ? "(?:.*/)?"
                    : recursive
                        ? ".*"
                        : "[^/]*");
                if (followedBySeparator)
                    index += 2;
                else if (recursive)
                    index++;
            }
            else if (character == '?')
            {
                expression.Append("[^/]");
            }
            else
            {
                expression.Append(Regex.Escape(character.ToString()));
            }
        }
        expression.Append('$');
        return expression.ToString();
    }
}
