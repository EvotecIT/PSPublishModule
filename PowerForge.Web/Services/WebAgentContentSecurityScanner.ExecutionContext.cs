using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex PersistentWorkingDirectoryChangeRegex = new(
        @"(?:^|&&|;|(?<!&)&(?!&)|\r?\n)\s*(?:(?:sudo|command)\s+)?(?:cd|chdir|pushd|popd|Set-Location|Push-Location|Pop-Location|sl)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PersistentRemoteExecutionContextRegex = new(
        @"(?:^|&&|;|(?<!&)&(?!&)|\r?\n)\s*(?:(?:sudo|command)\s+)?(?:" +
        @"(?:ssh|plink|mosh|winrs|psexec|wsl)(?=\s|$)|" +
        @"(?:docker|podman|nerdctl)(?:-compose)?\s+(?:(?:compose\s+)?(?:exec|run))\b|" +
        @"(?:kubectl|oc|lxc|incus)\s+exec\b|vagrant\s+ssh\b|" +
        @"Enter-PSSession\b|Invoke-Command\b|icm\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static bool HasRemoteExecutionWrapperPrefix(string content, int commandIndex)
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

        var tokens = Tokenize(ShellContinuationRegex.Replace(content[(start + 1)..commandIndex], " "));
        for (var index = 0; index < tokens.Length; index++)
        {
            var executable = Path.GetFileNameWithoutExtension(NormalizeToken(tokens[index]).Replace('\\', '/'));
            if (executable.Equals("ssh", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("plink", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("mosh", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("winrs", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("psexec", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("wsl", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("invoke-command", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("icm", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("enter-pssession", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("vagrant", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("lxc", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("incus", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("machinectl", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("nsenter", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("chroot", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("systemd-nspawn", StringComparison.OrdinalIgnoreCase))
                return true;

            if (executable.Equals("docker", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("docker-compose", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("podman", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("podman-compose", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("nerdctl", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("kubectl", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("oc", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private sealed class PackageExecutionFlowState
    {
        public bool WorkingDirectoryChanged { get; set; }
        public bool RemoteExecutionContextChanged { get; set; }
    }
}
