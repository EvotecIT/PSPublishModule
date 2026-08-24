namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
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

            string disabledHooksPath = IsWindows() ? "NUL" : "/dev/null";
            var checkout = RunBuildInputEvaluationProcess(
                "git",
                gitRoot!,
                new[]
                {
                    "-c",
                    "core.hooksPath=" + disabledHooksPath,
                    "worktree",
                    "add",
                    "--detach",
                    checkoutRoot,
                    revision!
                },
                environmentVariables: null,
                TimeSpan.FromMinutes(2));
            if (checkout.ExitCode != 0 || checkout.TimedOut || !File.Exists(controlledProjectPath))
                return false;

            string? controlledRevision = ReadGitText(checkoutRoot, "rev-parse HEAD");
            string? controlledStatus = ReadGitRawText(
                checkoutRoot,
                "status --porcelain=v1 -z --untracked-files=all");
            return string.Equals(revision, controlledRevision, StringComparison.OrdinalIgnoreCase) &&
                   controlledStatus is not null &&
                   controlledStatus.Length == 0;
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
