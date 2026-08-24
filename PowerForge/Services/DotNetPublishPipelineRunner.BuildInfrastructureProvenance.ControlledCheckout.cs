using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static bool TryCreateControlledBuildEnvironment(
        IReadOnlyDictionary<string, string?> environmentVariables,
        string gitRoot,
        string controlledSourceRoot,
        out IReadOnlyDictionary<string, string?> controlledEnvironment)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var inheritedVariables = Environment.GetEnvironmentVariables();
        foreach (object? key in inheritedVariables.Keys)
        {
            string? name = key?.ToString();
            if (!string.IsNullOrWhiteSpace(name) && !IsApprovedControlledBuildEnvironmentVariable(name!))
                values[name!] = null;
        }
        string environmentRoot = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(controlledSourceRoot))!,
            "environment");
        string configurationRoot = Directory.CreateDirectory(Path.Combine(environmentRoot, "config")).FullName;
        string cacheRoot = Directory.CreateDirectory(Path.Combine(environmentRoot, "cache")).FullName;
        values["APPDATA"] = configurationRoot;
        values["LOCALAPPDATA"] = cacheRoot;
        values["XDG_CONFIG_HOME"] = configurationRoot;
        values["XDG_CACHE_HOME"] = cacheRoot;
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

    private static bool IsApprovedControlledBuildEnvironmentVariable(string name)
        => name.Equals("PATH", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("SystemRoot", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("WINDIR", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ComSpec", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("TMP", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("TMPDIR", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("HOME", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("USERPROFILE", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("DOTNET_ROOT(x86)", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("PROCESSOR_ARCHITECTURE", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("PROCESSOR_ARCHITEW6432", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ProgramFiles", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ProgramFiles(x86)", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ProgramW6432", StringComparison.OrdinalIgnoreCase);

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

            if (!TryCollectControlledGitFilterNames(gitRoot!, revision!, out string[] filterNames))
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

            if (!HasOnlyControlledBuildFileInputs(checkoutRoot))
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

    internal static bool HasOnlyControlledBuildFileInputs(string checkoutRoot)
    {
        try
        {
            var pending = new Stack<string>();
            pending.Push(checkoutRoot);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string childDirectory in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                        return false;
                    pending.Push(childDirectory);
                }

                foreach (string path in Directory.EnumerateFiles(directory))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        return false;

                    string extension = Path.GetExtension(path);
                    if (extension.Equals(".rsp", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.ReadLines(path).Any(value => ContainsRootedBuildValue(value, checkoutRoot)))
                            return false;
                        continue;
                    }

                    bool knownProjectExtension =
                        extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".proj", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
                    XDocument document;
                    try
                    {
                        document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                    }
                    catch when (!knownProjectExtension)
                    {
                        continue;
                    }
                    if (!knownProjectExtension &&
                        (document.Root is null ||
                         !document.Root.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    if (document.DescendantNodes()
                        .OfType<XText>()
                        .Select(text => text.Value)
                        .Concat(document.Descendants().Attributes().Select(attribute => attribute.Value))
                        .Any(value => ContainsRootedBuildValue(value, checkoutRoot)))
                    {
                        return false;
                    }
                }
            }

            return true;
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
