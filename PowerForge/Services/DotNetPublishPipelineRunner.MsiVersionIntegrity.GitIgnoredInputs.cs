using System.Diagnostics;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static string[]? ReadIgnoredBuildInputPaths(
        string gitRoot,
        IEnumerable<string> buildInputs)
    {
        StringComparer comparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        string fullGitRoot = Path.GetFullPath(gitRoot);
        var inputsByRepository = new Dictionary<string, HashSet<string>>(comparer);
        var repositoryByDirectory = new Dictionary<string, string?>(comparer);
        foreach (string input in buildInputs)
        {
            string fullInput = Path.GetFullPath(input);
            string inputDirectory = Path.GetDirectoryName(fullInput)!;
            if (!repositoryByDirectory.TryGetValue(inputDirectory, out string? repositoryRoot))
            {
                repositoryRoot = ReadGitText(inputDirectory, "rev-parse --show-toplevel");
                repositoryByDirectory[inputDirectory] = repositoryRoot;
            }
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                return null;

            repositoryRoot = Path.GetFullPath(repositoryRoot!);
            if (!IsSameOrBelowBuildInputPath(repositoryRoot, fullGitRoot) ||
                !IsSameOrBelowBuildInputPath(fullInput, repositoryRoot) ||
                !IsRecordedNestedGitRepository(repositoryRoot, fullGitRoot))
            {
                return null;
            }

            string relativeInput = FrameworkCompatibility.GetRelativePath(
                    repositoryRoot,
                    fullInput)
                .Replace('\\', '/')
                .TrimStart('/');
            if (!inputsByRepository.TryGetValue(repositoryRoot, out HashSet<string>? paths))
            {
                paths = new HashSet<string>(comparer);
                inputsByRepository[repositoryRoot] = paths;
            }
            paths.Add(relativeInput);
        }

        var ignoredInputs = new List<string>();
        foreach (KeyValuePair<string, HashSet<string>> repository in inputsByRepository)
        {
            string? ignoredOutput = ReadIgnoredGitPaths(repository.Key, repository.Value);
            if (ignoredOutput is null)
                return null;
            foreach (string ignoredPath in ignoredOutput.Split(
                         new[] { '\0' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string fullIgnoredPath = Path.GetFullPath(Path.Combine(
                    repository.Key,
                    ignoredPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsSameOrBelowBuildInputPath(fullIgnoredPath, fullGitRoot))
                    return null;
                ignoredInputs.Add(FrameworkCompatibility.GetRelativePath(
                        fullGitRoot,
                        fullIgnoredPath)
                    .Replace('\\', '/')
                    .TrimStart('/'));
            }
        }
        return ignoredInputs.ToArray();
    }

    private static string? ReadIgnoredGitPaths(string gitRoot, IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0)
            return string.Empty;

        try
        {
            if (!TryResolveTrustedBuildTool("git", out string gitPath))
                return null;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = gitPath,
                    Arguments = "--no-replace-objects check-ignore -z --stdin",
                    WorkingDirectory = gitRoot,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (KeyValuePair<string, string?> variable in CreateTrustedGitEnvironment())
            {
                if (variable.Value is null)
                    process.StartInfo.EnvironmentVariables.Remove(variable.Key);
                else
                    process.StartInfo.EnvironmentVariables[variable.Key] = variable.Value;
            }
            if (!process.Start())
                return null;

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            Task input = process.StandardInput.WriteAsync(string.Join("\0", paths) + '\0');
            Task inputClosed = input.ContinueWith(
                _ => process.StandardInput.Close(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            if (!process.WaitForExit(5000))
            {
                try
                {
#if NET472
                    process.Kill();
#else
                    process.Kill(entireProcessTree: true);
#endif
                }
                catch
                {
                    // A failed or already-exited Git process still makes the query untrusted.
                }
                try
                {
                    inputClosed.Wait(1000);
                }
                catch
                {
                    // The process was terminated before it could consume the full request.
                }
                return null;
            }

            input.GetAwaiter().GetResult();
            inputClosed.GetAwaiter().GetResult();
            _ = error.GetAwaiter().GetResult();
            string ignored = output.GetAwaiter().GetResult();
            return process.ExitCode switch
            {
                0 => ignored,
                1 => string.Empty,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}
