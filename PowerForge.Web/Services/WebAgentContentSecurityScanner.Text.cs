using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex UrlRegex = new(
        "https?://[^\\s<>\\\"'(){}]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly (Regex Pattern, string Label)[] PromptInjectionPatterns =
    {
        (new Regex(@"\bignore\s+(?:all\s+)?(?:previous|prior|earlier)\s+(?:instructions?|prompts?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "ignore-prior-instructions"),
        (new Regex(@"\b(?:reveal|print|exfiltrate|send)\s+(?:the\s+)?(?:system\s+prompt|secrets?|credentials?|tokens?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "secret-exfiltration-directive")
    };
    private static readonly Regex RemoteExecutionRegex = new(
        @"\b(?:curl(?:\.exe)?|wget|Invoke-WebRequest|iwr|Invoke-RestMethod|irm)\b[^\r\n|;]*(?:\||;)\s*(?:sudo\s+)?(?:sh|bash|zsh|pwsh|powershell|iex|Invoke-Expression)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void ScanInvisibleUnicode(
        string content,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        int lineOffset = 0,
        bool countLogicalLines = true)
    {
        var line = 1;
        for (var index = 0; index < content.Length;)
        {
            if (countLogicalLines && content[index] == '\n')
                line++;

            if (!Rune.TryGetRuneAt(content, index, out var rune))
            {
                index++;
                continue;
            }

            var value = rune.Value;
            var isBidi = value is 0x061C or 0x200E or 0x200F or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;
            var isTag = value is >= 0xE0000 and <= 0xE007F;
            var isZeroWidth = value is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF;
            if (isBidi || isTag || isZeroWidth)
            {
                var kind = isBidi ? "bidirectional control" : isTag ? "Unicode tag character" : "zero-width control";
                AddFinding(findings, "error", "PFAGENT.TEXT.INVISIBLE_UNICODE", path, lineOffset + line,
                    $"Artifact contains a {kind} U+{value:X4}; machine-facing instructions must not contain invisible control characters.");
            }

            index += rune.Utf16SequenceLength;
        }
    }

    private static void ScanPromptInjection(
        string content,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        int lineOffset = 0,
        bool countLogicalLines = true)
    {
        foreach (var (pattern, label) in PromptInjectionPatterns)
        {
            foreach (Match match in pattern.Matches(content))
            {
                AddFinding(findings, "warning", "PFAGENT.TEXT.PROMPT_DIRECTIVE", path, GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    $"Potential agent-directed prompt injection phrase detected ({label}). Review the surrounding content.");
            }
        }

        foreach (Match match in RemoteExecutionRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.COMMAND.REMOTE_EXECUTION", path, GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                "Downloaded content is piped directly to an interpreter. Prefer a pinned, integrity-checked artifact and a separate execution step.");
        }
    }

    private static void ExtractUrls(string content, ISet<Uri> urls)
    {
        foreach (Match match in UrlRegex.Matches(content))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                urls.Add(uri);
        }
    }

    private static int GetLineNumber(string content, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < content.Length; i++)
        {
            if (content[i] == '\n')
                line++;
        }
        return line;
    }

    private static int GetReportedLine(string content, int index, int lineOffset, bool countLogicalLines)
        => lineOffset + (countLogicalLines ? GetLineNumber(content, index) : 1);
}
