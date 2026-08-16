namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly string[] PackageCommandIntroducers =
    [
        "run", "execute", "invoke", "try", "use", "using", "then", "and then", "with"
    ];

    private static bool IsPackageCommandInvocationContext(string content, int commandIndex)
    {
        if (commandIndex < 0 || commandIndex > content.Length)
            return false;

        var lineStart = commandIndex;
        while (lineStart > 0 && content[lineStart - 1] is not ('\r' or '\n'))
            lineStart--;

        var prefix = content[lineStart..commandIndex];
        if (prefix.EndsWith('`'))
            return true;

        var candidate = prefix.Trim();
        while (candidate.StartsWith('>'))
            candidate = candidate[1..].TrimStart();

        if (candidate.Length == 0 || candidate is "$" or "#" or "PS>" or "-" or "*" or "+")
            return true;

        if (candidate.Length >= 2 && candidate[0] is '-' or '*' or '+' && char.IsWhiteSpace(candidate[1]))
            return candidate[2..].Trim().Length == 0;

        var markerEnd = 0;
        while (markerEnd < candidate.Length && char.IsDigit(candidate[markerEnd]))
            markerEnd++;
        if (markerEnd > 0 && markerEnd + 1 < candidate.Length &&
            candidate[markerEnd] is '.' or ')' && char.IsWhiteSpace(candidate[markerEnd + 1]))
            return candidate[(markerEnd + 2)..].Trim().Length == 0;

        foreach (var introducer in PackageCommandIntroducers)
        {
            if (candidate.Equals(introducer, StringComparison.OrdinalIgnoreCase) ||
                candidate.EndsWith(" " + introducer, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return candidate.EndsWith('$') || candidate.EndsWith("PS>", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasShellCommandSeparatorPrefix(string content, int commandIndex)
    {
        var lineStart = commandIndex;
        while (lineStart > 0 && content[lineStart - 1] is not ('\r' or '\n'))
            lineStart--;

        var prefix = content[lineStart..commandIndex].TrimEnd();
        if (prefix.LastIndexOf("$(", StringComparison.Ordinal) > prefix.LastIndexOf(')'))
            return true;
        return prefix.EndsWith(';') ||
               prefix.EndsWith('|') ||
               prefix.EndsWith("&&", StringComparison.Ordinal) ||
               prefix.EndsWith("||", StringComparison.Ordinal);
    }

    private static string TrimMarkdownInlineCodeCommand(
        string content,
        int commandIndex,
        string matchedCommand)
    {
        var openingStart = commandIndex;
        while (openingStart > 0 && content[openingStart - 1] == '`')
            openingStart--;

        var delimiterLength = commandIndex - openingStart;
        if (delimiterLength == 0)
            return matchedCommand;

        var lineEnd = content.IndexOfAny(['\r', '\n'], commandIndex);
        if (lineEnd < 0)
            lineEnd = content.Length;

        for (var index = commandIndex; index < lineEnd; index++)
        {
            if (content[index] != '`')
                continue;

            var runLength = 1;
            while (index + runLength < lineEnd && content[index + runLength] == '`')
                runLength++;
            if (runLength == delimiterLength)
                return content.Substring(commandIndex, index - commandIndex);

            index += runLength - 1;
        }

        return matchedCommand;
    }
}
