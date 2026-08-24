using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class ControlledGitRepository
    {
        internal ControlledGitRepository(string gitDirectory, string revision)
        {
            GitDirectory = gitDirectory;
            Revision = revision;
        }

        internal string GitDirectory { get; }

        internal string Revision { get; }
    }

    private static bool TryCollectControlledGitFilterNames(
        string gitRoot,
        string revision,
        out string[] filterNames)
    {
        string? exactGitDirectory = ReadGitText(gitRoot, "rev-parse --absolute-git-dir");
        string? commonDirectory = ReadGitText(gitRoot, "rev-parse --git-common-dir");
        if (string.IsNullOrWhiteSpace(exactGitDirectory) ||
            string.IsNullOrWhiteSpace(commonDirectory))
        {
            filterNames = Array.Empty<string>();
            return false;
        }

        exactGitDirectory = Path.GetFullPath(exactGitDirectory!);
        string rootGitDirectory = Path.GetFullPath(Path.IsPathRooted(commonDirectory!)
            ? commonDirectory!
            : Path.Combine(gitRoot, commonDirectory!));
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!TryReadConfiguredGitFilterNames(gitRoot, exactGitDirectory, names))
        {
            filterNames = Array.Empty<string>();
            return false;
        }

        var pending = new Queue<ControlledGitRepository>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue(new ControlledGitRepository(rootGitDirectory, revision));
        while (pending.Count > 0)
        {
            ControlledGitRepository repository = pending.Dequeue();
            string visitKey = repository.GitDirectory + "\0" + repository.Revision;
            if (!visited.Add(IsWindows() ? visitKey.ToUpperInvariant() : visitKey))
                continue;
            if (!TryReadConfiguredGitFilterNames(gitRoot, repository.GitDirectory, names) ||
                !TryReadGitDirectoryText(
                    gitRoot,
                    repository.GitDirectory,
                    out string? tree,
                    "ls-tree",
                    "-r",
                    "-z",
                    repository.Revision))
            {
                filterNames = Array.Empty<string>();
                return false;
            }

            foreach (string entry in tree!.Split(
                         new[] { '\0' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                int tab = entry.IndexOf('\t');
                if (tab <= 0)
                    continue;
                string[] metadata = entry.Substring(0, tab).Split(' ');
                if (metadata.Length < 3 ||
                    !metadata[0].Equals("160000", StringComparison.Ordinal) ||
                    !metadata[1].Equals("commit", StringComparison.Ordinal))
                {
                    continue;
                }

                string modulePath = "modules/" + entry.Substring(tab + 1).Replace('\\', '/');
                if (!TryReadGitDirectoryText(
                        gitRoot,
                        repository.GitDirectory,
                        out string? childGitDirectory,
                        "rev-parse",
                        "--git-path",
                        modulePath) ||
                    string.IsNullOrWhiteSpace(childGitDirectory))
                {
                    filterNames = Array.Empty<string>();
                    return false;
                }

                childGitDirectory = Path.GetFullPath(Path.IsPathRooted(childGitDirectory!)
                    ? childGitDirectory!
                    : Path.Combine(gitRoot, childGitDirectory!));
                if (!Directory.Exists(childGitDirectory) ||
                    !IsSameOrBelowBuildInputPath(childGitDirectory, repository.GitDirectory) ||
                    !TryReadGitDirectoryText(
                        gitRoot,
                        childGitDirectory,
                        out _,
                        "cat-file",
                        "-e",
                        metadata[2] + "^{commit}"))
                {
                    filterNames = Array.Empty<string>();
                    return false;
                }

                pending.Enqueue(new ControlledGitRepository(childGitDirectory, metadata[2]));
            }
        }

        filterNames = names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool TryReadConfiguredGitFilterNames(
        string workingDirectory,
        string gitDirectory,
        ISet<string> filterNames)
    {
        if (!TryReadGitDirectoryText(
                workingDirectory,
                gitDirectory,
                out string? configuredNames,
                new[]
                {
                    "config",
                    "--name-only",
                    "--get-regexp",
                    "^filter\\..*\\.(clean|smudge|process|required)$"
                },
                acceptMissing: true))
        {
            return false;
        }

        foreach (string line in configuredNames!.Split(
                     new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            Match match = Regex.Match(
                line.Trim(),
                @"^filter\.(?<name>[A-Za-z0-9_.-]+)\.(?:clean|smudge|process|required)$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;
            filterNames.Add(match.Groups["name"].Value);
        }
        return true;
    }

    private static bool TryReadGitDirectoryText(
        string workingDirectory,
        string gitDirectory,
        out string? text,
        params string[] arguments)
        => TryReadGitDirectoryText(
            workingDirectory,
            gitDirectory,
            out text,
            arguments,
            acceptMissing: false);

    private static bool TryReadGitDirectoryText(
        string workingDirectory,
        string gitDirectory,
        out string? text,
        string[] arguments,
        bool acceptMissing)
    {
        var command = new List<string> { "--git-dir=" + gitDirectory };
        command.AddRange(arguments);
        var process = RunBuildInputEvaluationProcess(
            "git",
            workingDirectory,
            command,
            environmentVariables: null,
            TimeSpan.FromSeconds(30));
        if (process.TimedOut ||
            (process.ExitCode != 0 && (!acceptMissing || process.ExitCode != 1)))
        {
            text = null;
            return false;
        }

        text = process.StdOut.TrimEnd('\r', '\n');
        return true;
    }

    private static string[] BuildControlledGitArguments(
        IEnumerable<string> filterNames,
        params string[] command)
    {
        var arguments = new List<string>
        {
            "-c",
            "core.hooksPath=" + (IsWindows() ? "NUL" : "/dev/null"),
            "-c",
            "core.fsmonitor=false"
        };
        foreach (string filterName in filterNames)
        {
            arguments.Add("-c");
            arguments.Add("filter." + filterName + ".clean=");
            arguments.Add("-c");
            arguments.Add("filter." + filterName + ".smudge=");
            arguments.Add("-c");
            arguments.Add("filter." + filterName + ".process=");
            arguments.Add("-c");
            arguments.Add("filter." + filterName + ".required=false");
        }
        arguments.AddRange(command);
        return arguments.ToArray();
    }

    private static bool TryInitializeControlledSubmodules(
        string checkoutRoot,
        IReadOnlyCollection<string> filterNames)
    {
        if (!File.Exists(Path.Combine(checkoutRoot, ".gitmodules")))
            return true;

        string[] updateCommand =
        [
            "-c",
            "protocol.allow=never",
            "-c",
            "protocol.file.allow=always",
            "submodule",
            "update",
            "--init",
            "--recursive",
            "--no-fetch",
            "--checkout"
        ];
        var update = RunBuildInputEvaluationProcess(
            "git",
            checkoutRoot,
            BuildControlledGitArguments(filterNames, updateCommand),
            environmentVariables: null,
            TimeSpan.FromMinutes(2));
        if (update.ExitCode != 0 || update.TimedOut)
            return false;

        var status = RunBuildInputEvaluationProcess(
            "git",
            checkoutRoot,
            BuildControlledGitArguments(
                filterNames,
                "submodule",
                "status",
                "--recursive"),
            environmentVariables: null,
            TimeSpan.FromMinutes(1));
        return status.ExitCode == 0 &&
               !status.TimedOut &&
               status.StdOut.Split(
                       new[] { '\r', '\n' },
                       StringSplitOptions.RemoveEmptyEntries)
                   .All(line => line.Length > 0 && line[0] == ' ');
    }
}
