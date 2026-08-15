using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex DynamicExecutableInvocationRegex = new(
        @"(?:^|&&|;|(?<!&)&(?!&)|\||\r?\n|[""'])\s*(?:(?:sudo|doas|pkexec|runuser|runas|gosu|su-exec|cmd|env|command|exec|iex|Invoke-Expression)\b[^\r\n;&|]{0,160}?\s+)?(?:&\s*)?(?<command>[""']?(?:\$(?:env:)?[A-Za-z_][A-Za-z0-9_]*|\$\{[^}\r\n]+\}|%[A-Za-z_][A-Za-z0-9_]*%)[""']?)\s+(?<argument>[^\s;&|]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static void ScanDynamicExecutableInvocations(
        string content,
        string path,
        int lineOffset,
        bool countLogicalLines,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        foreach (Match match in DynamicExecutableInvocationRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                $"Dynamic executable invocation '{match.Groups["command"].Value}' cannot be proven to launch a canonical package manager.");
        }
    }
}
