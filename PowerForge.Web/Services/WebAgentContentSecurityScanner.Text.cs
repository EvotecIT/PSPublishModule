using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

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
        @"\b(?:curl(?:\.exe)?|wget|Invoke-WebRequest|iwr|Invoke-RestMethod|irm)\b[^\r\n|;&]*(?:\||;|&&)\s*(?:sudo\s+)?(?:(?:(?:[A-Za-z]:)?[\\/][^\s|;&]*[\\/])?(?:env|command)(?:\s+(?:-[^\s]+|[A-Za-z_][A-Za-z0-9_]*=[^\s]+))*\s+)?(?:(?:[A-Za-z]:)?[\\/][^\s|;&]*[\\/])?(?:sh|bash|zsh|pwsh|powershell|iex|Invoke-Expression|cmd|python(?:\d+(?:\.\d+)*)?|py|ruby|perl|node|php)(?:\.exe)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RemoteExecutionPowerShellExpressionRegex = new(
        @"\b(?:iex|Invoke-Expression)\b\s*(?:(?:\$\s*)?\(+|[""']\s*\$\()\s*(?:(?:curl(?:\.exe)?|wget|Invoke-WebRequest|iwr|Invoke-RestMethod|irm)\b|[^\r\n]{0,200}\bDownloadString\s*\()",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RemoteExecutionShellExpressionRegex = new(
        @"\b(?:eval\b\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:sh|bash|zsh|python(?:\d+(?:\.\d+)*)?|py)\b[^\r\n]{0,80}?-c\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:pwsh|powershell)\b[^\r\n]{0,80}?(?:-c|-Command)\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:node)\b[^\r\n]{0,80}?(?:-e|--eval|-p|--print)\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:ruby|perl)\b[^\r\n]{0,80}?(?:-e|--eval)\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|php\b[^\r\n]{0,80}?-r\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:sh|bash|zsh)\b\s*<\(\s*(?:curl(?:\.exe)?|wget)\b|(?:source\b|\.)\s*<\(\s*(?:curl(?:\.exe)?|wget)\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RemoteExecutionScriptBlockRegex = new(
        @"(?:&|Invoke-Command\b[^\r\n]{0,80})\s*\(\s*\[scriptblock\]::Create\s*\(\s*\(*\s*(?:curl(?:\.exe)?|wget|Invoke-WebRequest|iwr|Invoke-RestMethod|irm)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RemoteExecutionDotSourceProcessSubstitutionRegex = new(
        @"(?:^|[;&]\s*|\s)\.\s*<\(\s*(?:curl(?:\.exe)?|wget)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly ConditionalWeakTable<string, int[]> LineStartsByContent = new();

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

    }

    private static void ScanRemoteExecution(
        string content,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        int lineOffset = 0,
        bool countLogicalLines = true)
    {
        var normalized = ShellContinuationRegex.Replace(content, static match => new string(' ', match.Length));
        foreach (var pattern in new[]
                 {
                     RemoteExecutionRegex,
                     RemoteExecutionPowerShellExpressionRegex,
                     RemoteExecutionShellExpressionRegex,
                     RemoteExecutionScriptBlockRegex,
                     RemoteExecutionDotSourceProcessSubstitutionRegex
                 })
        {
            foreach (Match match in pattern.Matches(normalized))
            {
                AddFinding(findings, "error", "PFAGENT.COMMAND.REMOTE_EXECUTION", path, GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "Downloaded content is passed directly to an interpreter. Prefer a pinned, integrity-checked artifact and a separate execution step.");
            }
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
        var lineStarts = LineStartsByContent.GetValue(content, static value =>
        {
            var starts = new List<int> { 0 };
            for (var position = 0; position < value.Length; position++)
            {
                if (value[position] == '\n' && position + 1 < value.Length)
                    starts.Add(position + 1);
            }
            return starts.ToArray();
        });
        var boundedIndex = Math.Clamp(index, 0, content.Length);
        var result = Array.BinarySearch(lineStarts, boundedIndex);
        return result >= 0 ? result + 1 : ~result;
    }

    private static int GetReportedLine(string content, int index, int lineOffset, bool countLogicalLines)
        => lineOffset + (countLogicalLines ? GetLineNumber(content, index) : 1);
}
