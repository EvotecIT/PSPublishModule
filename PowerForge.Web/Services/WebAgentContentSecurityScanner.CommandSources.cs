using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private const string PowerShellPackageSourceDefaultKeyPattern =
        @"['""][^'""\r\n]{0,160}:(?:Repo(?:sitory)?|Sou(?:rce)?|Provider(?:Name)?|Proxy(?:Credential)?)['""]";
    private const string PowerShellDefaultParameterValuesReferencePattern =
        @"\$(?:(?:global|script|local|private):)?PSDefaultParameterValues";
    private static readonly Regex PowerShellIndexedPackageSourceDefaultRegex = new(
        PowerShellDefaultParameterValuesReferencePattern + @"\s*(?:\[\s*" + PowerShellPackageSourceDefaultKeyPattern +
        @"\s*\]\s*=|\.(?:Add|Set_Item)\s*\(\s*" + PowerShellPackageSourceDefaultKeyPattern + @")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PowerShellBulkPackageSourceDefaultRegex = new(
        @"(?:" + PowerShellDefaultParameterValuesReferencePattern + @"\s*=|\b(?:Set|New)-Variable\b[^\r\n]{0,160}\bPSDefaultParameterValues\b|\b(?:Set|New)-Item\b[^\r\n]{0,160}\bVariable:[\\/]?(?:(?:global|script|local|private):)?PSDefaultParameterValues\b)[\s\S]{0,1024}?" +
        PowerShellPackageSourceDefaultKeyPattern,
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void ScanPowerShellPackageSourceDefaults(
        string content,
        string path,
        int lineOffset,
        bool countLogicalLines,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        foreach (var pattern in new[]
                 {
                     PowerShellIndexedPackageSourceDefaultRegex,
                     PowerShellBulkPackageSourceDefaultRegex
                 })
        {
            foreach (Match match in pattern.Matches(content))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path,
                    GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "PowerShell default parameter values cannot override package repositories, sources, providers, or proxies; configured values are redacted.");
            }
        }
    }

    private static bool ValidatePackageSourceOptions(
        string ecosystem,
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (IsPowerShellPackageCommand(tokens[0]) &&
            tokens.Skip(1).Any(static token => token.StartsWith('@')))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                "PowerShell splatted package-command arguments can hide repository, source, provider, or proxy parameters and cannot be verified statically.");
            return false;
        }

        for (var index = 1; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var separator = token.IndexOf('=');
            if (separator < 0 && ecosystem == "powershellgallery")
                separator = token.IndexOf(':');
            var option = separator > 0 ? token[..separator] : token;
            if (ecosystem == "npm" && IsNpmProjectRootOption(option))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                    $"Node package project-root option '{option}' can select uninspected registry configuration.");
                return false;
            }
            if (ecosystem == "npm" &&
                (tokens[0] is "bun" or "bunx") &&
                (option.Equals("-c", StringComparison.OrdinalIgnoreCase) ||
                 option.Equals("--config", StringComparison.OrdinalIgnoreCase)))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                    $"Bun configuration option '{option}' can select uninspected registry configuration; the configured value is redacted.");
                return false;
            }
            if (ecosystem == "packagist" &&
                (option.Equals("-d", StringComparison.OrdinalIgnoreCase) ||
                 option.Equals("--working-dir", StringComparison.OrdinalIgnoreCase)))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                    $"Composer project-root option '{option}' can select uninspected repository, plugin, and dependency configuration.");
                return false;
            }
            if (ecosystem == "pypi" && (option.Equals("-r", StringComparison.Ordinal) ||
                                        option.Equals("--requirement", StringComparison.OrdinalIgnoreCase) ||
                                        option.Equals("-c", StringComparison.Ordinal) ||
                                        option.Equals("--constraint", StringComparison.OrdinalIgnoreCase) ||
                                        option.Equals("--group", StringComparison.OrdinalIgnoreCase)))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND", path, line,
                    $"Python dependency input '{option}' can introduce packages that are not statically verifiable from the command.");
                return false;
            }
            if (ecosystem == "pypi" && IsPythonTransportTrustOption(option))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                    $"Python transport-trust option '{option}' can bypass canonical registry certificate or host validation; the configured value is redacted.");
                return false;
            }
            if (ecosystem == "pypi" && option.Equals("--python", StringComparison.OrdinalIgnoreCase))
            {
                var pythonExecutable = separator > 0
                    ? token[(separator + 1)..]
                    : index + 1 < tokens.Length ? tokens[index + 1] : string.Empty;
                if (!IsCanonicalPythonExecutable(pythonExecutable))
                {
                    AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_ENVIRONMENT", path, line,
                        "Python interpreter selectors must use a literal canonical Python launcher; paths, environments, and dynamic values cannot be verified.");
                    return false;
                }
            }
            if (!IsPackageSourceOption(ecosystem, option))
                continue;

            var value = separator > 0
                ? token[(separator + 1)..]
                : index + 1 < tokens.Length ? tokens[index + 1] : string.Empty;
            if (IsCanonicalPackageSource(ecosystem, option, value))
                continue;

            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                string.IsNullOrWhiteSpace(value)
                    ? $"Package source option '{option}' does not have a statically verifiable public-registry value."
                    : $"Package source option '{option}' redirects installation away from the canonical public registry; the configured value is redacted.");
            return false;
        }
        return true;
    }

    private static bool IsPowerShellPackageCommand(string executable)
        => executable is "install-package" or "update-package" or "install-module" or "save-module" or
            "install-script" or "update-script" or "save-script" or "install-psresource" or
            "save-psresource" or "update-module" or "update-psresource";

    private static bool IsPackageSourceOption(string ecosystem, string option)
        => ecosystem switch
        {
            "nuget" => option.Equals("--source", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("-Source", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("--add-source", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("--configfile", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("-ConfigFile", StringComparison.OrdinalIgnoreCase),
            "powershellgallery" => IsPowerShellRepositoryOption(option),
            "npm" => option.Equals("--registry", StringComparison.OrdinalIgnoreCase) ||
                     option.Equals("--userconfig", StringComparison.OrdinalIgnoreCase) ||
                     option.Equals("--globalconfig", StringComparison.OrdinalIgnoreCase),
             "pypi" => option.Equals("--index-url", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("-i", StringComparison.Ordinal) ||
                       option.Equals("--index", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("--default-index", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("--extra-index-url", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("--find-links", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("-f", StringComparison.Ordinal) ||
                      option.Equals("--config-file", StringComparison.OrdinalIgnoreCase),
            "crates" => option.Equals("--registry", StringComparison.OrdinalIgnoreCase) ||
                        option.Equals("--index", StringComparison.OrdinalIgnoreCase) ||
                        option.Equals("--config", StringComparison.OrdinalIgnoreCase),
            "rubygems" => option.Equals("--source", StringComparison.OrdinalIgnoreCase) ||
                          option.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                          option.Equals("--local", StringComparison.OrdinalIgnoreCase) ||
                          option.Equals("--config-file", StringComparison.OrdinalIgnoreCase),
            "packagist" => option.Equals("--repository", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static bool IsNpmProjectRootOption(string option)
        => option.Equals("--prefix", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--workspace", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--workspaces", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--directory", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--dir", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--cwd", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-C", StringComparison.Ordinal);

    private static bool IsPythonTransportTrustOption(string option)
        => option.Equals("--trusted-host", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--cert", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--client-cert", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--allow-insecure-host", StringComparison.OrdinalIgnoreCase);

    private static bool IsCanonicalPythonExecutable(string value)
        => Regex.IsMatch(
            NormalizeToken(value),
            @"^(?:python(?:\d+(?:\.\d+)*)?|py)(?:\.exe)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsPowerShellRepositoryOption(string option)
        => option.Equals("-Repository", StringComparison.OrdinalIgnoreCase) ||
            option.Length >= 4 && "-Repository".StartsWith(option, StringComparison.OrdinalIgnoreCase);

    private static bool IsCanonicalPackageSource(string ecosystem, string option, string value)
    {
        value = NormalizeToken(value).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ecosystem switch
        {
            "nuget" when option.Equals("--source", StringComparison.OrdinalIgnoreCase) ||
                          option.Equals("-Source", StringComparison.OrdinalIgnoreCase) ||
                          option.Equals("-s", StringComparison.OrdinalIgnoreCase) =>
                value.Equals("nuget.org", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("https://api.nuget.org/v3/index.json", StringComparison.OrdinalIgnoreCase),
            "powershellgallery" => value.Equals("PSGallery", StringComparison.OrdinalIgnoreCase),
            "npm" => value.Equals("https://registry.npmjs.org", StringComparison.OrdinalIgnoreCase),
            "pypi" when option.Equals("--index-url", StringComparison.OrdinalIgnoreCase) ||
                        option.Equals("-i", StringComparison.Ordinal) ||
                        option.Equals("--index", StringComparison.OrdinalIgnoreCase) ||
                        option.Equals("--default-index", StringComparison.OrdinalIgnoreCase) =>
                value.Equals("https://pypi.org/simple", StringComparison.OrdinalIgnoreCase),
            "crates" when option.Equals("--registry", StringComparison.OrdinalIgnoreCase) =>
                value.Equals("crates-io", StringComparison.OrdinalIgnoreCase),
            "rubygems" => value.Equals("https://rubygems.org", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
