using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool IsMsBuildPropertyFunctionExpression(string value)
        => value.IndexOf("$([", StringComparison.Ordinal) >= 0;

    internal static bool TryMatchProjectReferenceGlob(
        string definingDirectory,
        string? itemSpec,
        string referencedPath)
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
        string fullReferencedPath = Path.GetFullPath(referencedPath);
        string patternRoot = Path.GetPathRoot(fullPattern) ?? string.Empty;
        string referencedRoot = Path.GetPathRoot(fullReferencedPath) ?? string.Empty;
        if (!FileSystemPathSafety.ExistingPathComparer.Equals(
                patternRoot,
                referencedRoot))
        {
            return false;
        }

        string[] patternComponents = SplitPathComponents(fullPattern, patternRoot);
        string[] referencedComponents = SplitPathComponents(fullReferencedPath, referencedRoot);
        return MatchProjectReferenceGlobComponents(
            patternComponents,
            0,
            referencedComponents,
            0,
            referencedRoot);
    }

    private static string[] SplitPathComponents(string path, string root)
        => path.Substring(root.Length)
            .Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchProjectReferenceGlobComponents(
        IReadOnlyList<string> pattern,
        int patternIndex,
        IReadOnlyList<string> referenced,
        int referencedIndex,
        string referencedRoot)
    {
        if (patternIndex == pattern.Count)
            return referencedIndex == referenced.Count;

        string componentPattern = pattern[patternIndex];
        if (componentPattern == "**")
        {
            for (int index = referencedIndex; index <= referenced.Count; index++)
            {
                if (MatchProjectReferenceGlobComponents(
                        pattern,
                        patternIndex + 1,
                        referenced,
                        index,
                        referencedRoot))
                {
                    return true;
                }
            }
            return false;
        }

        if (referencedIndex == referenced.Count)
            return false;

        string expression = BuildProjectReferenceGlobExpression(componentPattern);
        string referencedComponent = referenced[referencedIndex];
        bool matches = Regex.IsMatch(
            referencedComponent,
            expression,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!matches)
        {
            string referencedComponentPath = referenced
                .Take(referencedIndex + 1)
                .Aggregate(referencedRoot, Path.Combine);
            matches = ExistingChildLookupIsCaseInsensitive(referencedComponentPath) &&
                Regex.IsMatch(
                    referencedComponent,
                    expression,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1));
        }
        return matches && MatchProjectReferenceGlobComponents(
            pattern,
            patternIndex + 1,
            referenced,
            referencedIndex + 1,
            referencedRoot);
    }

    private static bool ExistingChildLookupIsCaseInsensitive(string childPath)
    {
        string trimmedPath = childPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string childName = Path.GetFileName(trimmedPath);
        string? parent = Path.GetDirectoryName(trimmedPath);
        if (string.IsNullOrWhiteSpace(childName) || string.IsNullOrWhiteSpace(parent))
            return false;

        char[] alternateName = childName.ToCharArray();
        int index = Array.FindIndex(alternateName, character =>
            char.ToUpperInvariant(character) != char.ToLowerInvariant(character));
        if (index < 0)
            return false;
        alternateName[index] = char.IsUpper(alternateName[index])
            ? char.ToLowerInvariant(alternateName[index])
            : char.ToUpperInvariant(alternateName[index]);
        string alternatePath = Path.Combine(parent!, new string(alternateName));
        return FileSystemPathSafety.ExistingPathComparer.Equals(trimmedPath, alternatePath);
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
