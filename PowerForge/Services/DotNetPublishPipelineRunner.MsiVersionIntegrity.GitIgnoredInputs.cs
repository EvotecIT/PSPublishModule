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
        var verifiedRepositories = new HashSet<string>(comparer);
        foreach (string input in buildInputs)
        {
            string fullInput = Path.GetFullPath(input);
            string inputDirectory = Path.GetDirectoryName(fullInput)!;
            if (!TryResolveBuildInputRepository(
                    inputDirectory,
                    fullGitRoot,
                    repositoryByDirectory,
                    out string repositoryRoot))
                return null;

            bool verifyRepository = verifiedRepositories.Add(repositoryRoot);
            if (!IsSameOrBelowBuildInputPath(repositoryRoot, fullGitRoot) ||
                !IsSameOrBelowBuildInputPath(fullInput, repositoryRoot) ||
                (verifyRepository &&
                 !comparer.Equals(repositoryRoot, fullGitRoot) &&
                 !IsRecordedNestedGitRepository(repositoryRoot, fullGitRoot)))
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

    private static bool TryResolveBuildInputRepository(
        string inputDirectory,
        string outerGitRoot,
        Dictionary<string, string?> repositoryByDirectory,
        out string repositoryRoot)
    {
        repositoryRoot = string.Empty;
        string current = Path.GetFullPath(inputDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string outerRoot = Path.GetFullPath(outerGitRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var traversed = new List<string>();

        while (IsSameOrBelowBuildInputPath(current, outerRoot))
        {
            if (repositoryByDirectory.TryGetValue(current, out string? cachedRoot))
            {
                if (string.IsNullOrWhiteSpace(cachedRoot))
                    return false;
                repositoryRoot = cachedRoot!;
                CacheBuildInputRepositoryDirectories(
                    repositoryByDirectory,
                    traversed,
                    repositoryRoot);
                return true;
            }

            traversed.Add(current);
            string gitMarker = Path.Combine(current, ".git");
            if (File.Exists(gitMarker) || Directory.Exists(gitMarker))
            {
                string? resolvedRoot = ReadGitText(current, "rev-parse --show-toplevel");
                if (string.IsNullOrWhiteSpace(resolvedRoot))
                {
                    CacheBuildInputRepositoryDirectories(repositoryByDirectory, traversed, null);
                    return false;
                }

                resolvedRoot = Path.GetFullPath(resolvedRoot!)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(resolvedRoot, current, comparison))
                {
                    CacheBuildInputRepositoryDirectories(repositoryByDirectory, traversed, null);
                    return false;
                }

                repositoryRoot = resolvedRoot;
                CacheBuildInputRepositoryDirectories(
                    repositoryByDirectory,
                    traversed,
                    repositoryRoot);
                return true;
            }

            if (string.Equals(current, outerRoot, comparison))
            {
                repositoryRoot = outerRoot;
                CacheBuildInputRepositoryDirectories(
                    repositoryByDirectory,
                    traversed,
                    repositoryRoot);
                return true;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, comparison))
                break;
            current = parent;
        }

        CacheBuildInputRepositoryDirectories(repositoryByDirectory, traversed, null);
        return false;
    }

    private static void CacheBuildInputRepositoryDirectories(
        Dictionary<string, string?> repositoryByDirectory,
        IEnumerable<string> directories,
        string? repositoryRoot)
    {
        foreach (string directory in directories)
            repositoryByDirectory[directory] = repositoryRoot;
    }

    private static string? ReadIgnoredGitPaths(string gitRoot, IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0)
            return string.Empty;

        try
        {
            string gitPath = ResolveGitChildExecutable("git");
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
