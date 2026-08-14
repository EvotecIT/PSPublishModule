using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex[] PackageExecutableShadowingPatterns =
    {
        new(@"\bfunction\s+(?:(?:global|script|local|private):)?(?<name>[A-Za-z][A-Za-z0-9_.-]*)\s*(?:\(\s*\))?\s*\{",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(?:^|[;&]\s*|\s)(?<name>[A-Za-z][A-Za-z0-9_.-]*)\s*\(\s*\)\s*\{",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline),
        new(@"\balias\s+(?<name>[A-Za-z][A-Za-z0-9_.-]*)\s*=",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?:Set|New)-Alias\b[^\r\n;&|]*?\s-Name(?::|\s+)\s*['""]?(?<name>[A-Za-z][A-Za-z0-9_.-]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?:Set|New)-Alias\b\s+['""]?(?<name>[A-Za-z][A-Za-z0-9_.-]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?:Set|New)-Item\b[^\r\n;&|]*?(?:-Path(?::|\s+)\s*)?['""]?(?:Alias|Function)(?::|[\\/])(?<name>[A-Za-z][A-Za-z0-9_.-]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bSet-Content\b[^\r\n;&|]*?(?:-Path(?::|\s+)\s*)?['""]?(?:Alias|Function)(?::|[\\/])(?<name>[A-Za-z][A-Za-z0-9_.-]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bdoskey(?:\.exe)?\s+(?<name>[A-Za-z][A-Za-z0-9_.-]*)\s*=",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bhash\s+-p\s+[^\s;&|]+\s+(?<name>[A-Za-z][A-Za-z0-9_.-]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    };

    private static void ScanPackageExecutableShadowing(
        string content,
        string path,
        ICollection<WebAgentContentSecurityFinding> findings,
        int lineOffset,
        bool countLogicalLines)
    {
        foreach (var pattern in PackageExecutableShadowingPatterns)
        {
            foreach (Match match in pattern.Matches(content))
            {
                var name = NormalizeExecutable(match.Groups["name"].Value);
                if (!IsSupportedPackageExecutable(name))
                    continue;

                AddFinding(findings, "error", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND", path,
                    GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    $"Package-manager executable '{name}' is redefined or redirected in the artifact; commands must resolve to the canonical launcher.");
            }
        }
    }
}
