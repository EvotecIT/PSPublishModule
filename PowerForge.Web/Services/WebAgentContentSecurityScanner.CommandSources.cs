namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static bool ValidatePackageSourceOptions(
        string ecosystem,
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
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
            if (ecosystem == "pypi" && (option.Equals("-r", StringComparison.OrdinalIgnoreCase) ||
                                        option.Equals("--requirement", StringComparison.OrdinalIgnoreCase) ||
                                        option.Equals("-c", StringComparison.OrdinalIgnoreCase) ||
                                        option.Equals("--constraint", StringComparison.OrdinalIgnoreCase) ||
                                        option.Equals("--group", StringComparison.OrdinalIgnoreCase)))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND", path, line,
                    $"Python dependency input '{option}' can introduce packages that are not statically verifiable from the command.");
                return false;
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
                    : $"Package source option '{option}' redirects installation to untrusted source '{value}'.");
            return false;
        }
        return true;
    }

    private static bool IsPackageSourceOption(string ecosystem, string option)
        => ecosystem switch
        {
            "nuget" => option.Equals("--source", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("-Source", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("--add-source", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("--configfile", StringComparison.OrdinalIgnoreCase),
            "powershellgallery" => IsPowerShellRepositoryOption(option),
            "npm" => option.Equals("--registry", StringComparison.OrdinalIgnoreCase) ||
                     option.Equals("--userconfig", StringComparison.OrdinalIgnoreCase) ||
                     option.Equals("--globalconfig", StringComparison.OrdinalIgnoreCase),
             "pypi" => option.Equals("--index-url", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("-i", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("--index", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("--default-index", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("--extra-index-url", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("--find-links", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("--config-file", StringComparison.OrdinalIgnoreCase),
            "crates" => option.Equals("--registry", StringComparison.OrdinalIgnoreCase) ||
                        option.Equals("--index", StringComparison.OrdinalIgnoreCase),
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
           option.Equals("-C", StringComparison.OrdinalIgnoreCase);

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
                        option.Equals("-i", StringComparison.OrdinalIgnoreCase) ||
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
