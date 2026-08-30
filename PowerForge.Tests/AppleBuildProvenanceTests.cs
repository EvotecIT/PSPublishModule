using System.Diagnostics;
using PowerForge;

namespace PowerForge.Tests;

public sealed class AppleBuildProvenanceTests
{
    [Fact]
    public void ResolveLocalSourceRevision_distinguishes_clean_and_dirty_builds()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.AppleBuildProvenance",
            Guid.NewGuid().ToString("N")));
        try
        {
            RunGit(root.FullName, "init");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            File.WriteAllText(Path.Combine(root.FullName, "tracked.txt"), "clean");
            RunGit(root.FullName, "add", "tracked.txt");
            RunGit(root.FullName, "commit", "-m", "fixture");
            var head = RunGit(root.FullName, "rev-parse", "HEAD").Trim().ToLowerInvariant();

            Assert.Equal(head, AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));

            File.WriteAllText(Path.Combine(root.FullName, "untracked.txt"), "dirty");
            Assert.Equal(head + "-dirty", AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveLocalSourceRevision_fails_closed_when_status_cannot_be_read()
    {
        var requestIndex = 0;
        var runner = new StubProcessRunner(request =>
        {
            requestIndex++;
            return requestIndex == 1
                ? new ProcessRunResult(
                    0,
                    new string('a', 40),
                    string.Empty,
                    request.FileName,
                    TimeSpan.Zero,
                    timedOut: false)
                : new ProcessRunResult(
                    128,
                    string.Empty,
                    "status unavailable",
                    request.FileName,
                    TimeSpan.Zero,
                    timedOut: false);
        });
        var git = GitClient.CreateTrustedSystemClient(runner, TimeSpan.FromSeconds(10));

        Assert.Null(AppleBuildProvenance.ResolveLocalSourceRevision(
            Directory.GetCurrentDirectory(),
            git));
    }

    [Fact]
    public void RejectIgnoredBuildInputs_rejects_inputs_copied_to_the_build()
    {
        var root = CreateRepository();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "local.xcconfig\n.build/\n");
            RunGit(root.FullName, "add", ".gitignore");
            RunGit(root.FullName, "commit", "-m", "ignore local input");
            File.WriteAllText(Path.Combine(root.FullName, "local.xcconfig"), "SETTING = local");
            Directory.CreateDirectory(Path.Combine(root.FullName, ".build"));
            File.WriteAllText(Path.Combine(root.FullName, ".build", "cache"), "generated");

            var exception = Assert.Throws<InvalidOperationException>(
                () => AppleBuildProvenance.RejectIgnoredBuildInputs(root.FullName));

            Assert.Contains("local.xcconfig", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(".build/cache", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static DirectoryInfo CreateRepository()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.AppleBuildProvenance",
            Guid.NewGuid().ToString("N")));
        RunGit(root.FullName, "init");
        RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
        RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
        File.WriteAllText(Path.Combine(root.FullName, "tracked.txt"), Guid.NewGuid().ToString("N"));
        RunGit(root.FullName, "add", "tracked.txt");
        RunGit(root.FullName, "commit", "-m", "fixture");
        return root;
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);
        return output;
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        internal StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request));
    }
}
