using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static void EnsureNoExecutableSwiftStringInterpolation(string packageRoot, string source)
    {
        for (var index = 0; index < source.Length; index++)
        {
            if (!TryFindSwiftStringBounds(source, index, out var contentStart, out var endExclusive, out var hashCount))
                continue;

            for (var cursor = contentStart; cursor < endExclusive; cursor++)
            {
                if (source[cursor] != '\\')
                    continue;
                var marker = cursor + 1;
                var hashes = 0;
                while (marker < endExclusive && source[marker] == '#')
                {
                    hashes++;
                    marker++;
                }
                if (hashes == hashCount && marker < endExclusive && source[marker] == '(' &&
                    (hashCount > 0 || !IsEscapedOrdinarySwiftBackslash(source, cursor)))
                {
                    throw new InvalidOperationException(
                        $"Local Swift package '{packageRoot}' uses executable string interpolation, whose manifest expression cannot be proven safely. " +
                        "Use literal manifest declarations before creating an exact-source Apple checkpoint.");
                }
            }
            index = endExclusive - 1;
        }
    }

    private static bool IsEscapedOrdinarySwiftBackslash(string source, int slashIndex)
    {
        var backslashes = 0;
        for (var index = slashIndex - 1; index >= 0 && source[index] == '\\'; index--)
            backslashes++;
        return backslashes % 2 == 1;
    }

    private static void ValidateDirectSwiftPackageDependencyFactories(string packageRoot, string manifestSyntax)
    {
        foreach (Match reference in Regex.Matches(
                     manifestSyntax,
                     "\\.\\s*(?:package\\b|`package`|binaryTarget\\b|`binaryTarget`)",
                     RegexOptions.CultureInvariant))
        {
            var next = reference.Index + reference.Length;
            while (next < manifestSyntax.Length && char.IsWhiteSpace(manifestSyntax[next]))
                next++;
            if (next < manifestSyntax.Length && manifestSyntax[next] == '(')
                continue;

            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' references a package or binary target factory indirectly, so its external source cannot be proven. " +
                "Invoke each factory directly with literal source identity and integrity arguments.");
        }
    }

    private static IReadOnlyList<SwiftPackageDependencyCall> ParseDirectSwiftPackageDependencyCalls(
        string source,
        string syntax)
    {
        var calls = new List<SwiftPackageDependencyCall>();
        foreach (Match reference in Regex.Matches(
                     syntax,
                     "\\.\\s*(?:package\\b|`package`)",
                     RegexOptions.CultureInvariant))
        {
            var openingParenthesis = reference.Index + reference.Length;
            while (openingParenthesis < syntax.Length && char.IsWhiteSpace(syntax[openingParenthesis]))
                openingParenthesis++;
            if (openingParenthesis >= syntax.Length || syntax[openingParenthesis] != '(')
                continue;

            var closingParenthesis = FindMatchingSwiftDelimiter(syntax, openingParenthesis, '(', ')');
            var arguments = ParseTopLevelSwiftArguments(
                source.Substring(openingParenthesis + 1, closingParenthesis - openingParenthesis - 1),
                syntax.Substring(openingParenthesis + 1, closingParenthesis - openingParenthesis - 1));
            calls.Add(new SwiftPackageDependencyCall(arguments));
        }
        return calls;
    }

    private static IReadOnlyDictionary<string, string> ParseTopLevelSwiftArguments(string source, string syntax)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        var start = 0;
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;
        for (var index = 0; index <= syntax.Length; index++)
        {
            var character = index < syntax.Length ? syntax[index] : ',';
            switch (character)
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
            }
            if (character != ',' || parentheses != 0 || brackets != 0 || braces != 0)
                continue;

            AddTopLevelSwiftArgument(
                arguments,
                source.Substring(start, index - start),
                syntax.Substring(start, index - start));
            start = index + 1;
        }
        return arguments;
    }

    private static void AddTopLevelSwiftArgument(
        IDictionary<string, string> arguments,
        string source,
        string syntax)
    {
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;
        for (var index = 0; index < syntax.Length; index++)
        {
            switch (syntax[index])
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
                case ':' when parentheses == 0 && brackets == 0 && braces == 0:
                    var label = syntax.Substring(0, index).Trim();
                    if (label.Length >= 2 && label[0] == '`' && label[label.Length - 1] == '`')
                        label = label.Substring(1, label.Length - 2);
                    if (!Regex.IsMatch(label, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                        return;
                    if (arguments.ContainsKey(label))
                        throw new InvalidOperationException($"Swift package dependency repeats top-level argument '{label}'.");
                    arguments.Add(label, source.Substring(index + 1).Trim());
                    return;
            }
        }
    }

    private static int FindMatchingSwiftDelimiter(string syntax, int openingIndex, char opening, char closing)
    {
        var depth = 0;
        for (var index = openingIndex; index < syntax.Length; index++)
        {
            if (syntax[index] == opening)
                depth++;
            else if (syntax[index] == closing && --depth == 0)
                return index;
        }
        throw new InvalidOperationException("Swift package manifest contains an unterminated dependency declaration.");
    }

    private static bool TryReadLiteralSwiftString(string value, out string literal)
    {
        var match = Regex.Match(
            value,
            "^\\s*\"(?<literal>[^\"\\\\\\r\\n]+)\"\\s*$",
            RegexOptions.CultureInvariant);
        literal = match.Success ? match.Groups["literal"].Value : string.Empty;
        return match.Success;
    }

    private static bool ContainsSwiftIdentifier(string syntax, string identifier)
        => Regex.IsMatch(
            syntax,
            $"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);

    private static bool ContainsSwiftMemberReference(string syntax, string identifier)
        => Regex.IsMatch(
            syntax,
            $"\\.\\s*(?:{Regex.Escape(identifier)}\\b|`{Regex.Escape(identifier)}`)",
            RegexOptions.CultureInvariant);

    private static string RemoveSwiftComments(string source)
    {
        var result = source.ToCharArray();
        for (var index = 0; index < source.Length; index++)
        {
            if (TryFindSwiftStringEnd(source, index, out var stringEnd))
            {
                index = stringEnd - 1;
                continue;
            }
            if (source[index] != '/' || index + 1 >= source.Length)
                continue;
            if (source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\r' && source[index] != '\n')
                    result[index++] = ' ';
                index--;
                continue;
            }
            if (source[index + 1] != '*')
                continue;

            var depth = 1;
            MaskSwiftTrivia(result, index, 2);
            index += 2;
            while (index < source.Length && depth > 0)
            {
                if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '*')
                {
                    MaskSwiftTrivia(result, index, 2);
                    depth++;
                    index += 2;
                    continue;
                }
                if (index + 1 < source.Length && source[index] == '*' && source[index + 1] == '/')
                {
                    MaskSwiftTrivia(result, index, 2);
                    depth--;
                    index += 2;
                    continue;
                }
                MaskSwiftTrivia(result, index, 1);
                index++;
            }
            index--;
        }
        return new string(result);
    }

    private static string MaskSwiftStringLiterals(string source)
    {
        var result = source.ToCharArray();
        for (var index = 0; index < source.Length; index++)
        {
            if (!TryFindSwiftStringEnd(source, index, out var stringEnd))
                continue;

            MaskSwiftTrivia(result, index, stringEnd - index);
            index = stringEnd - 1;
        }
        return new string(result);
    }

    private static bool TryFindSwiftStringEnd(string source, int start, out int endExclusive)
    {
        return TryFindSwiftStringBounds(source, start, out _, out endExclusive, out _);
    }

    private static bool TryFindSwiftStringBounds(
        string source,
        int start,
        out int contentStart,
        out int endExclusive,
        out int hashCount)
    {
        contentStart = start;
        endExclusive = start;
        var quoteIndex = start;
        while (quoteIndex < source.Length && source[quoteIndex] == '#')
            quoteIndex++;
        hashCount = quoteIndex - start;
        if (quoteIndex >= source.Length || source[quoteIndex] != '"')
            return false;

        var quoteCount = quoteIndex + 2 < source.Length &&
                         source[quoteIndex + 1] == '"' &&
                         source[quoteIndex + 2] == '"'
            ? 3
            : 1;
        var cursor = quoteIndex + quoteCount;
        contentStart = cursor;
        while (cursor < source.Length)
        {
            if (MatchesSwiftStringDelimiter(source, cursor, quoteCount, hashCount) &&
                !IsEscapedSwiftStringDelimiter(source, cursor, hashCount))
            {
                endExclusive = cursor + quoteCount + hashCount;
                return true;
            }
            cursor++;
        }

        endExclusive = source.Length;
        return true;
    }

    private static bool MatchesSwiftStringDelimiter(string source, int start, int quoteCount, int hashCount)
    {
        if (start + quoteCount + hashCount > source.Length)
            return false;
        for (var offset = 0; offset < quoteCount; offset++)
        {
            if (source[start + offset] != '"')
                return false;
        }
        for (var offset = 0; offset < hashCount; offset++)
        {
            if (source[start + quoteCount + offset] != '#')
                return false;
        }
        return true;
    }

    private static bool IsEscapedSwiftStringDelimiter(string source, int quoteIndex, int hashCount)
    {
        if (hashCount > 0)
        {
            var escapeStart = quoteIndex - hashCount - 1;
            if (escapeStart < 0 || source[escapeStart] != '\\')
                return false;
            for (var offset = 0; offset < hashCount; offset++)
            {
                if (source[escapeStart + 1 + offset] != '#')
                    return false;
            }
            return true;
        }

        var backslashes = 0;
        for (var index = quoteIndex - 1; index >= 0 && source[index] == '\\'; index--)
            backslashes++;
        return backslashes % 2 == 1;
    }

    private static void MaskSwiftTrivia(char[] value, int start, int length)
    {
        var end = Math.Min(value.Length, start + length);
        for (var index = start; index < end; index++)
        {
            if (value[index] != '\r' && value[index] != '\n')
                value[index] = ' ';
        }
    }

    private sealed class SwiftPackageDependencyCall
    {
        internal SwiftPackageDependencyCall(IReadOnlyDictionary<string, string> arguments)
        {
            Arguments = arguments;
        }

        internal IReadOnlyDictionary<string, string> Arguments { get; }
    }
}
