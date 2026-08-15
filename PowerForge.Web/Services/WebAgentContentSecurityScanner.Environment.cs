using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private const string PackageSourceEnvironmentNamePattern =
        @"(?:HOME|USERPROFILE|HOMEDRIVE|HOMEPATH|APPDATA|LOCALAPPDATA|XDG_CONFIG_HOME|XDG_DATA_HOME|XDG_CACHE_HOME|NPM_CONFIG_[A-Za-z0-9_]+|YARN_(?:[A-Za-z0-9_]*REGISTRY[A-Za-z0-9_]*|RC_FILENAME)|PIP_INDEX_URL|PIP_EXTRA_INDEX_URL|PIP_FIND_LINKS|PIP_CONFIG_FILE|PIP_REQUIREMENT|PIP_CONSTRAINT|PIP_BUILD_CONSTRAINT|PIP_GROUP|PIP_EDITABLE|PIP_TRUSTED_HOST|PIP_CERT|PIP_CLIENT_CERT|UV_INDEX_URL|UV_EXTRA_INDEX_URL|UV_DEFAULT_INDEX|UV_INDEX|UV_FIND_LINKS|UV_CONSTRAINT|UV_OVERRIDE|UV_BUILD_CONSTRAINT|UV_CONFIG_FILE|UV_INSECURE_HOST|NODE_TLS_REJECT_UNAUTHORIZED|NODE_EXTRA_CA_CERTS|CURL_CA_BUNDLE|SSL_CERT_FILE|SSL_CERT_DIR|REQUESTS_CA_BUNDLE|GIT_SSL_NO_VERIFY|BUN_INSTALL_REGISTRY|GEM_HOST|BUNDLE_MIRROR__[A-Za-z0-9_]+|COMPOSER|COMPOSER_HOME|COMPOSER_AUTH|COMPOSER_REPO_PACKAGIST|CARGO_HOME|CARGO_NET_OFFLINE|CARGO_REGISTRY_DEFAULT|CARGO_REGISTRIES_[A-Za-z0-9_]+_INDEX)";
    private const string RuntimeInjectionEnvironmentNamePattern =
        @"(?:NODE_OPTIONS|DOTNET_STARTUP_HOOKS|CORECLR_ENABLE_PROFILING|CORECLR_PROFILER_PATH|PYTHONPATH|PYTHONSTARTUP|RUBYOPT|RUBYLIB|BUNDLE_GEMFILE)";
    private const string CommandResolutionEnvironmentNamePattern = @"(?:PATH|PATHEXT|PSModulePath)";
    private static readonly Regex CommandEnvironmentPrefixRegex = new(
        @"(?:^|\s)(?:env\s+)?(?:[A-Za-z_][A-Za-z0-9_]*=(?:'[^']*'|""[^""]*""|[^\s;&|]+)\s*)+(?:env\s+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PackageSourceEnvironmentRegex = new(
        $@"(?<![A-Za-z0-9_])(?:\$env:)?{PackageSourceEnvironmentNamePattern}\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RuntimeInjectionEnvironmentRegex = new(
        $@"(?<![A-Za-z0-9_])(?:\$env:)?{RuntimeInjectionEnvironmentNamePattern}\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PackageSourceEnvironmentUtilityWriteRegex = new(
        BuildShellEnvironmentUtilityWritePattern(PackageSourceEnvironmentNamePattern),
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RuntimeInjectionEnvironmentUtilityWriteRegex = new(
        BuildShellEnvironmentUtilityWritePattern(RuntimeInjectionEnvironmentNamePattern),
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PowerShellPackageSourceEnvironmentWriteRegex = new(
        BuildPowerShellEnvironmentWritePattern(PackageSourceEnvironmentNamePattern),
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PowerShellRuntimeInjectionEnvironmentWriteRegex = new(
        BuildPowerShellEnvironmentWritePattern(RuntimeInjectionEnvironmentNamePattern),
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CommandResolutionEnvironmentRegex = new(
        $@"(?:^|[;&]\s*|\b(?:export|set|env)\s+|\$env:)['""]?(?<name>{CommandResolutionEnvironmentNamePattern})\s*\+?=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PowerShellCommandResolutionEnvironmentWriteRegex = new(
        BuildPowerShellEnvironmentWritePattern(CommandResolutionEnvironmentNamePattern),
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CommandResolutionUtilityWriteRegex = new(
        $@"(?:{BuildShellEnvironmentUtilityWritePattern(CommandResolutionEnvironmentNamePattern)}|\b(?:declare|typeset)\s+(?:-[A-Za-z]+\s+)+{CommandResolutionEnvironmentNamePattern}\s*=)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void ScanPackageSourceEnvironmentOverrides(
        string content,
        string path,
        int lineOffset,
        bool countLogicalLines,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        foreach (Match match in PackageSourceEnvironmentRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                $"Package source environment override '{match.Value.TrimEnd('=')}' is not allowed in machine-facing installation instructions.");
        }
        foreach (Match match in PowerShellPackageSourceEnvironmentWriteRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                "PowerShell environment-provider writes cannot change package sources in machine-facing installation instructions.");
        }
        foreach (Match match in PackageSourceEnvironmentUtilityWriteRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                "Shell environment commands cannot change package sources in machine-facing installation instructions.");
        }
    }

    private static void ScanRuntimeInjectionEnvironmentOverrides(
        string content,
        string path,
        int lineOffset,
        bool countLogicalLines,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        foreach (Match match in RuntimeInjectionEnvironmentRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.COMMAND.RUNTIME_INJECTION", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                $"Runtime injection environment variable '{match.Value.TrimEnd('=')}' is not allowed in machine-facing instructions.");
        }
        foreach (Match match in PowerShellRuntimeInjectionEnvironmentWriteRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.COMMAND.RUNTIME_INJECTION", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                "PowerShell environment-provider writes cannot configure runtime injection in machine-facing instructions.");
        }
        foreach (Match match in RuntimeInjectionEnvironmentUtilityWriteRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.COMMAND.RUNTIME_INJECTION", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                "Shell environment commands cannot configure runtime injection in machine-facing instructions.");
        }
    }

    private static void ScanCommandResolutionEnvironmentOverrides(
        string content,
        string path,
        int lineOffset,
        bool countLogicalLines,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        foreach (Match match in CommandResolutionEnvironmentRegex.Matches(content))
        {
            var name = match.Groups["name"].Value;
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_ENVIRONMENT", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                $"Persistent command-resolution environment override '{name}' can redirect package-manager launchers.");
        }
        foreach (Match match in PowerShellCommandResolutionEnvironmentWriteRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_ENVIRONMENT", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                "PowerShell environment-provider writes cannot change command resolution before package-manager instructions.");
        }
        foreach (Match match in CommandResolutionUtilityWriteRegex.Matches(content))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_ENVIRONMENT", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                "Shell environment commands cannot change command resolution before package-manager instructions.");
        }
    }

    private static string BuildPowerShellEnvironmentWritePattern(string namePattern)
        => $@"(?:\b(?:Set-Item|New-Item|Set-Content|si|ni)\b[^\r\n;|]{{0,160}}?(?:-Path\s+|-LiteralPath\s+)?[""']?Env:[\\/]?{namePattern}\b|\[\s*(?:System\.)?Environment\s*\]::SetEnvironmentVariable\s*\(\s*[""']{namePattern}[""'])";

    private static string BuildShellEnvironmentUtilityWritePattern(string namePattern)
        => $@"(?:\bsetx(?:\.exe)?\s+['""]?{namePattern}\b|\bset\s+(?:-[A-Za-z]+\s+)+{namePattern}\b|\bsetenv\s+{namePattern}\b)";

    private static bool HasCommandScopedEnvironmentPrefix(string content, int commandIndex)
    {
        var start = commandIndex - 1;
        while (start >= 0)
        {
            if (content[start] is ';' or '&' or '|')
                break;
            if (content[start] is '\r' or '\n')
            {
                var previous = start - 1;
                if (content[start] == '\n' && previous >= 0 && content[previous] == '\r')
                    previous--;
                while (previous >= 0 && content[previous] is ' ' or '\t')
                    previous--;
                if (previous < 0 || content[previous] is not ('\\' or '`'))
                    break;
                start = previous - 1;
                continue;
            }
            start--;
        }
        var prefix = ShellContinuationRegex.Replace(content[(start + 1)..commandIndex], " ");
        return CommandEnvironmentPrefixRegex.IsMatch(prefix);
    }

    private static bool HasWorkingDirectoryWrapperPrefix(string content, int commandIndex)
    {
        var start = commandIndex - 1;
        while (start >= 0 && content[start] is not (';' or '&' or '|' or '\r' or '\n'))
            start--;
        var tokens = Tokenize(content[(start + 1)..commandIndex]);
        var envIndex = Array.FindIndex(tokens, static token => token.Equals("env", StringComparison.OrdinalIgnoreCase));
        if (envIndex >= 0 && tokens.Skip(envIndex + 1).Any(static token =>
            token.Equals("-C", StringComparison.Ordinal) ||
            token.StartsWith("-C", StringComparison.Ordinal) && token.Length > 2 ||
            token.Equals("--chdir", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("--chdir=", StringComparison.OrdinalIgnoreCase)))
            return true;

        var sudoIndex = Array.FindIndex(tokens, static token => token.Equals("sudo", StringComparison.OrdinalIgnoreCase));
        return sudoIndex >= 0 && tokens.Skip(sudoIndex + 1).Any(static token =>
            token.Equals("-D", StringComparison.Ordinal) ||
            token.StartsWith("-D", StringComparison.Ordinal) && token.Length > 2 ||
            token.Equals("-R", StringComparison.Ordinal) ||
            token.StartsWith("-R", StringComparison.Ordinal) && token.Length > 2 ||
            token.Equals("--chdir", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("--chdir=", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("--chroot", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("--chroot=", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasDataAppendingWrapperPrefix(string content, int commandIndex)
    {
        var start = commandIndex - 1;
        while (start >= 0)
        {
            if (content[start] is ';' or '&' or '|')
                break;
            if (content[start] is '\r' or '\n')
            {
                var previous = start - 1;
                if (content[start] == '\n' && previous >= 0 && content[previous] == '\r')
                    previous--;
                while (previous >= 0 && content[previous] is ' ' or '\t')
                    previous--;
                if (previous < 0 || content[previous] is not ('\\' or '`' or '^'))
                    break;
                start = previous - 1;
                continue;
            }
            start--;
        }

        var prefix = ShellContinuationRegex.Replace(content[(start + 1)..commandIndex], " ");
        var tokens = Tokenize(prefix);
        var hasDataAppendingExecutable = tokens.Any(static token =>
        {
            var executable = Path.GetFileNameWithoutExtension(NormalizeToken(token).Replace('\\', '/'));
            return executable.Equals("xargs", StringComparison.OrdinalIgnoreCase) ||
                   executable.Equals("gxargs", StringComparison.OrdinalIgnoreCase) ||
                   executable.Equals("parallel", StringComparison.OrdinalIgnoreCase) ||
                   executable.Equals("gparallel", StringComparison.OrdinalIgnoreCase);
        });
        if (hasDataAppendingExecutable)
            return true;

        var firstExecutable = tokens.Length == 0
            ? string.Empty
            : Path.GetFileNameWithoutExtension(NormalizeToken(tokens[0]).Replace('\\', '/'));
        return (firstExecutable.Equals("find", StringComparison.OrdinalIgnoreCase) ||
                firstExecutable.Equals("gfind", StringComparison.OrdinalIgnoreCase)) &&
               tokens.Any(static token => token.Equals("-exec", StringComparison.OrdinalIgnoreCase) ||
                                          token.Equals("-execdir", StringComparison.OrdinalIgnoreCase));
    }
}
