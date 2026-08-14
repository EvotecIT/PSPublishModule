using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex CommandEnvironmentPrefixRegex = new(
        @"(?:^|\s)(?:env\s+)?(?:[A-Za-z_][A-Za-z0-9_]*=(?:'[^']*'|""[^""]*""|[^\s;&|]+)\s*)+(?:env\s+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PackageSourceEnvironmentRegex = new(
        @"(?<![A-Za-z0-9_])(?:\$env:)?(?:NPM_CONFIG_[A-Za-z0-9_]+|YARN_(?:[A-Za-z0-9_]*REGISTRY[A-Za-z0-9_]*|RC_FILENAME)|PIP_INDEX_URL|PIP_EXTRA_INDEX_URL|PIP_FIND_LINKS|PIP_CONFIG_FILE|PIP_REQUIREMENT|PIP_CONSTRAINT|PIP_BUILD_CONSTRAINT|PIP_GROUP|PIP_EDITABLE|UV_INDEX_URL|UV_EXTRA_INDEX_URL|UV_DEFAULT_INDEX|UV_INDEX|UV_FIND_LINKS|UV_CONSTRAINT|UV_OVERRIDE|UV_BUILD_CONSTRAINT|UV_CONFIG_FILE|BUN_INSTALL_REGISTRY|GEM_HOST|BUNDLE_MIRROR__[A-Za-z0-9_]+|CARGO_REGISTRIES_[A-Za-z0-9_]+_INDEX)\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RuntimeInjectionEnvironmentRegex = new(
        @"(?<![A-Za-z0-9_])(?:\$env:)?(?:NODE_OPTIONS|DOTNET_STARTUP_HOOKS|CORECLR_ENABLE_PROFILING|CORECLR_PROFILER_PATH|PYTHONPATH|PYTHONSTARTUP|RUBYOPT|RUBYLIB|BUNDLE_GEMFILE)\s*=",
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
    }

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
}
