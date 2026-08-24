using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryCreateControlledBuildEnvironment(
        IReadOnlyDictionary<string, string?> environmentVariables,
        string gitRoot,
        string controlledSourceRoot,
        out IReadOnlyDictionary<string, string?> controlledEnvironment)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string?> variable in environmentVariables)
        {
            if (variable.Value is null)
            {
                values[variable.Key] = null;
                continue;
            }
            if (!TryRemapControlledBuildValue(
                    variable.Value,
                    gitRoot,
                    controlledSourceRoot,
                    out string remappedValue))
            {
                controlledEnvironment = values;
                return false;
            }
            values[variable.Key] = remappedValue;
        }
        controlledEnvironment = values;
        return true;
    }

    private static bool TryRemapControlledBuildValue(
        string value,
        string gitRoot,
        string controlledSourceRoot,
        out string remappedValue)
    {
        string[] segments = value.Split(';');
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            int start = 0;
            while (start < segment.Length && char.IsWhiteSpace(segment[start]))
                start++;
            int end = segment.Length;
            while (end > start && char.IsWhiteSpace(segment[end - 1]))
                end--;
            char quote = '\0';
            if (end - start >= 2 &&
                (segment[start] == '\'' || segment[start] == '"') &&
                segment[end - 1] == segment[start])
            {
                quote = segment[start];
                start++;
                end--;
            }

            string candidate = segment.Substring(start, end - start);
            if (Path.IsPathRooted(candidate))
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(candidate);
                }
                catch
                {
                    remappedValue = string.Empty;
                    return false;
                }
                if (!IsSameOrBelowBuildInputPath(fullPath, gitRoot))
                {
                    remappedValue = string.Empty;
                    return false;
                }
                string relativePath = FrameworkCompatibility.GetRelativePath(gitRoot, fullPath);
                string controlledPath = Path.GetFullPath(Path.Combine(controlledSourceRoot, relativePath));
                if (!IsSameOrBelowBuildInputPath(controlledPath, controlledSourceRoot))
                {
                    remappedValue = string.Empty;
                    return false;
                }

                string prefix = segment.Substring(0, quote == '\0' ? start : start - 1);
                string suffix = segment.Substring(quote == '\0' ? end : end + 1);
                segments[index] = prefix +
                    (quote == '\0' ? string.Empty : quote.ToString()) +
                    controlledPath +
                    (quote == '\0' ? string.Empty : quote.ToString()) +
                    suffix;
                continue;
            }

            if (ContainsRootedBuildValue(candidate, gitRoot))
            {
                remappedValue = string.Empty;
                return false;
            }
        }

        remappedValue = string.Join(";", segments);
        return true;
    }

    private static bool ContainsRootedBuildValue(string value, string gitRoot)
    {
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (value.IndexOf(Path.GetFullPath(gitRoot), comparison) >= 0)
            return true;

        for (int index = 0; index < value.Length; index++)
        {
            if (index > 0 &&
                !char.IsWhiteSpace(value[index - 1]) &&
                "=,|([{'\"".IndexOf(value[index - 1]) < 0)
            {
                continue;
            }
            string candidate = value.Substring(index).TrimStart('\'', '"');
            if (Path.IsPathRooted(candidate))
                return true;
        }
        return false;
    }

    private static bool TryCollectControlledGitFilterNames(
        string gitRoot,
        out string[] filterNames)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        var visited = new HashSet<string>(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        pending.Enqueue(Path.GetFullPath(gitRoot));
        while (pending.Count > 0)
        {
            string repositoryRoot = pending.Dequeue();
            if (!visited.Add(repositoryRoot))
                continue;
            if (!TryReadConfiguredGitFilterNames(repositoryRoot, names))
            {
                filterNames = Array.Empty<string>();
                return false;
            }

            string? index = ReadGitRawText(repositoryRoot, "ls-files --stage -z");
            if (index is null)
            {
                filterNames = Array.Empty<string>();
                return false;
            }
            foreach (string entry in index.Split(
                         new[] { '\0' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                int tab = entry.IndexOf('\t');
                if (tab <= 0)
                    continue;
                string[] metadata = entry.Substring(0, tab).Split(' ');
                if (metadata.Length < 2 || !metadata[0].Equals("160000", StringComparison.Ordinal))
                    continue;

                string submoduleRoot = Path.GetFullPath(Path.Combine(
                    repositoryRoot,
                    entry.Substring(tab + 1)));
                string? submoduleRevision = Directory.Exists(submoduleRoot)
                    ? ReadGitText(submoduleRoot, "rev-parse HEAD")
                    : null;
                if (!IsSameOrBelowBuildInputPath(submoduleRoot, repositoryRoot) ||
                    !string.Equals(metadata[1], submoduleRevision, StringComparison.OrdinalIgnoreCase))
                {
                    filterNames = Array.Empty<string>();
                    return false;
                }
                pending.Enqueue(submoduleRoot);
            }
        }

        filterNames = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    private static bool TryReadConfiguredGitFilterNames(
        string repositoryRoot,
        ISet<string> filterNames)
    {
        var process = RunBuildInputEvaluationProcess(
            "git",
            repositoryRoot,
            new[]
            {
                "config",
                "--name-only",
                "--get-regexp",
                "^filter\\..*\\.(clean|smudge|process|required)$"
            },
            environmentVariables: null,
            TimeSpan.FromSeconds(30));
        if (process.TimedOut || (process.ExitCode != 0 && process.ExitCode != 1))
            return false;

        foreach (string line in process.StdOut.Split(
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

    private static string[] BuildControlledGitArguments(
        IEnumerable<string> filterNames,
        params string[] command)
    {
        var arguments = new List<string>
        {
            "-c",
            "core.hooksPath=" + (IsWindows() ? "NUL" : "/dev/null")
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
            "--no-fetch"
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

    private static bool TryCreateControlledSourceCheckout(
        string projectPath,
        string checkoutRoot,
        out string? gitRoot,
        out string? controlledProjectPath)
    {
        gitRoot = null;
        controlledProjectPath = null;
        try
        {
            string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            gitRoot = ReadGitText(projectDirectory, "rev-parse --show-toplevel");
            string? revision = ReadGitText(projectDirectory, "rev-parse HEAD");
            if (string.IsNullOrWhiteSpace(gitRoot) ||
                string.IsNullOrWhiteSpace(revision) ||
                !IsSameOrBelowBuildInputPath(projectPath, gitRoot!))
            {
                return false;
            }

            string relativeProjectPath = FrameworkCompatibility.GetRelativePath(
                Path.GetFullPath(gitRoot!),
                Path.GetFullPath(projectPath));
            controlledProjectPath = Path.GetFullPath(Path.Combine(checkoutRoot, relativeProjectPath));
            if (!IsSameOrBelowBuildInputPath(controlledProjectPath, checkoutRoot))
                return false;

            if (!TryCollectControlledGitFilterNames(gitRoot!, out string[] filterNames))
                return false;
            var checkout = RunBuildInputEvaluationProcess(
                "git",
                gitRoot!,
                BuildControlledGitArguments(
                    filterNames,
                    "worktree",
                    "add",
                    "--detach",
                    checkoutRoot,
                    revision!),
                environmentVariables: null,
                TimeSpan.FromMinutes(2));
            if (checkout.ExitCode != 0 || checkout.TimedOut || !File.Exists(controlledProjectPath))
                return false;

            if (!TryInitializeControlledSubmodules(checkoutRoot, filterNames))
                return false;

            string? controlledRevision = ReadGitText(checkoutRoot, "rev-parse HEAD");
            var controlledStatus = RunBuildInputEvaluationProcess(
                "git",
                checkoutRoot,
                BuildControlledGitArguments(
                    filterNames,
                    "status",
                    "--porcelain=v1",
                    "-z",
                    "--untracked-files=all"),
                environmentVariables: null,
                TimeSpan.FromMinutes(1));
            return string.Equals(revision, controlledRevision, StringComparison.OrdinalIgnoreCase) &&
                   controlledStatus.ExitCode == 0 &&
                   !controlledStatus.TimedOut &&
                   controlledStatus.StdOut.Length == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveControlledSourceCheckout(
        string? gitRoot,
        string checkoutRoot)
    {
        if (string.IsNullOrWhiteSpace(gitRoot))
            return;

        try
        {
            RunBuildInputEvaluationProcess(
                "git",
                gitRoot!,
                new[] { "worktree", "remove", "--force", checkoutRoot },
                environmentVariables: null,
                TimeSpan.FromMinutes(2));
        }
        catch
        {
            // The task-owned checkout is removed below and then pruned from Git metadata.
        }

        try
        {
            if (Directory.Exists(checkoutRoot))
                Directory.Delete(checkoutRoot, recursive: true);
        }
        catch
        {
            // Temporary checkout cleanup is best effort.
        }

        try
        {
            RunBuildInputEvaluationProcess(
                "git",
                gitRoot!,
                new[] { "worktree", "prune" },
                environmentVariables: null,
                TimeSpan.FromMinutes(2));
        }
        catch
        {
            // Temporary worktree metadata cleanup is best effort.
        }
    }
}
