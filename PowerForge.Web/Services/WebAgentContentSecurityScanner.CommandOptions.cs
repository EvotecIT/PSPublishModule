namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static bool OptionConsumesValue(string option)
        => option.Equals("--index-url", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--extra-index-url", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--prefix", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--workspace", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--directory", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-C", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-r", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--requirement", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-c", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--constraint", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--registry", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--userconfig", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--globalconfig", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--source", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--index", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Version", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-RequiredVersion", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--tag", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--group", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--pip-args", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--python", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-X", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--repository", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Repository", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-i", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--find-links", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--config-file", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--timeout", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--retries", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--trusted-host", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--cert", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--client-cert", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--cache-dir", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--log", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--omit", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--include", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--color", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--scope", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Scope", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--framework", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--arch", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--runtime", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--project", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--configfile", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--tool-manifest", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--tool-path", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--add-source", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-MinimumVersion", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-MaximumVersion", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Credential", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Proxy", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-ProxyCredential", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Destination", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-DestinationPath", StringComparison.OrdinalIgnoreCase);

    private static bool OptionIsFlag(string option)
        => option.Equals("--global", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--local", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--prerelease", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-restore", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--interactive", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--ignore-failed-sources", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-cache", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-q", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--isolated", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--disable-pip-version-check", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-color", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-input", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--save-dev", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-D", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-save", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-audit", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-fund", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--package-lock-only", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--legacy-peer-deps", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--strict-peer-deps", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--foreground-scripts", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--workspaces", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--include-workspace-root", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--exact", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-E", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--ignore-scripts", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--user", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--upgrade", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-U", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--pre", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-deps", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--dev", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--build", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--optional", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--locked", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--force", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Force", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--user-install", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--clear-sources", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--include-apps", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--include-deps", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--system-site-packages", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-document", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-interaction", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--update-with-all-dependencies", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-W", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-AllowClobber", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-SkipPublisherCheck", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-AcceptLicense", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-TrustRepository", StringComparison.OrdinalIgnoreCase);
}
