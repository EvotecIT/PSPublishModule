using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private const string PackageConfigurationPathPattern =
        @"(?:\.npmrc|\.yarnrc(?:\.yml)?|pip\.(?:conf|ini)|\.pypirc|nuget\.config|\.gemrc|\.bundle[\\/]config|\.cargo[\\/]config(?:\.toml)?|pypoetry[\\/]config\.toml|uv\.toml|auth\.json|composer\.json|pyproject\.toml|package\.json|gemfile|cargo\.toml)";
    private static readonly Regex PackageConfigurationRedirectRegex = new(
        @">{1,2}\s*['""]?[^\s'""<>|;&]*" + PackageConfigurationPathPattern + @"(?=['""\s;&|]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PackageConfigurationWriterRegex = new(
        @"\b(?:tee|Set-Content|Add-Content|Out-File|New-Item|Copy-Item|Move-Item|cp|copy|mv|move|dd|sed|perl)\b[^\r\n|;&]*" +
        PackageConfigurationPathPattern + @"(?=['""\s;&|]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PackageConfigurationFileApiRegex = new(
        @"\b(?:WriteAllText|WriteAllBytes|AppendAllText|AppendAllLines)\s*\([^\r\n)]*" +
        PackageConfigurationPathPattern + @"(?=['""\s,)]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void ScanPackageConfigurationWrites(
        string content,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        int lineOffset = 0,
        bool countLogicalLines = true)
    {
        var normalized = ShellContinuationRegex.Replace(content, static match => new string(' ', match.Length));
        foreach (var pattern in new[]
                 {
                     PackageConfigurationRedirectRegex,
                     PackageConfigurationWriterRegex,
                     PackageConfigurationFileApiRegex
                 })
        {
            foreach (Match match in pattern.Matches(normalized))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path,
                    GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "Direct writes to package-manager configuration files can redirect later dependency resolution; configuration values are redacted.");
            }
        }
    }
}
