using PowerForge;

namespace PowerForge.Tests;

public sealed class GitClientTests
{
    [Fact]
    public void ExistingFilePhysicalStatus_reports_identity_and_metadata_change_token()
    {
        var path = Path.GetTempFileName();
        try
        {
            var status = ExistingFilePathIdentityResolver.ResolveStatus(path);

            Assert.False(string.IsNullOrWhiteSpace(status.ChangeToken));
            Assert.False(string.IsNullOrWhiteSpace(status.Identity));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetStatusAsync_ParsesBranchAheadBehindAndChangeCounts()
    {
        const string output = """
# branch.head codex/release-flow
# branch.upstream origin/codex/release-flow
# branch.ab +2 -1
1 .M N... 100644 100644 100644 123456 123456 file.cs
? notes.txt
""";

        var runner = new StubProcessRunner(_ => new ProcessRunResult(
            exitCode: 0,
            stdOut: output,
            stdErr: string.Empty,
            executable: "git",
            duration: TimeSpan.FromSeconds(1),
            timedOut: false));
        var client = new GitClient(runner);

        var snapshot = await client.GetStatusAsync(@"C:\repo");

        Assert.True(snapshot.IsGitRepository);
        Assert.Equal("codex/release-flow", snapshot.BranchName);
        Assert.Equal("origin/codex/release-flow", snapshot.UpstreamBranch);
        Assert.Equal(2, snapshot.AheadCount);
        Assert.Equal(1, snapshot.BehindCount);
        Assert.Equal(1, snapshot.TrackedChangeCount);
        Assert.Equal(1, snapshot.UntrackedChangeCount);
    }

    [Fact]
    public async Task CreateBranchAsync_BuildsTypedGitArguments()
    {
        ProcessRunRequest? captured = null;
        var runner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(0, string.Empty, string.Empty, "git", TimeSpan.Zero, timedOut: false);
        });
        var client = new GitClient(runner);

        var result = await client.CreateBranchAsync(@"C:\repo", "codex/pspublishmodule-release-flow");

        Assert.NotNull(captured);
        Assert.Equal("git", captured!.FileName);
        Assert.Equal(@"C:\repo", captured.WorkingDirectory);
        Assert.Equal(["switch", "-c", "codex/pspublishmodule-release-flow"], captured.Arguments);
        Assert.Equal("git switch -c codex/pspublishmodule-release-flow", result.DisplayCommand);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetRemoteUrlAsync_BuildsTypedGitArguments()
    {
        ProcessRunRequest? captured = null;
        var runner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(0, "https://github.com/EvotecIT/PSPublishModule.git", string.Empty, "git", TimeSpan.Zero, timedOut: false);
        });
        var client = new GitClient(runner);

        var result = await client.GetRemoteUrlAsync(@"C:\repo");

        Assert.NotNull(captured);
        Assert.Equal(["remote", "get-url", "origin"], captured!.Arguments);
        Assert.Equal("git remote get-url origin", result.DisplayCommand);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task TrustedSystemClient_UsesFixedExecutableAndIsolatedGitEnvironment()
    {
        ProcessRunRequest? captured = null;
        var runner = new StubProcessRunner(request =>
        {
            captured = request;
            return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var client = GitClient.CreateTrustedSystemClient(runner, TimeSpan.FromSeconds(30));

        await client.RunRawAsync(Directory.GetCurrentDirectory(), ["status", "--short"]);

        Assert.NotNull(captured);
        Assert.True(Path.IsPathFullyQualified(captured!.FileName));
        Assert.Equal(Path.DirectorySeparatorChar == '\\' ? "git.exe" : "git", Path.GetFileName(captured.FileName));
        if (Path.DirectorySeparatorChar != '\\')
            Assert.Equal("/usr/bin/git", captured.FileName);
        Assert.False(captured.InheritEnvironment);
        Assert.NotNull(captured.EnvironmentVariables);
        Assert.False(captured.EnvironmentVariables!.ContainsKey("PATH"));
        Assert.False(captured.EnvironmentVariables.ContainsKey("GIT_DIR"));
        Assert.Equal("1", captured.EnvironmentVariables["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("/dev/null", captured.EnvironmentVariables["GIT_CONFIG_GLOBAL"]);
        Assert.Equal("0", captured.EnvironmentVariables["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("core.hooksPath", captured.EnvironmentVariables["GIT_CONFIG_KEY_0"]);
        Assert.Equal("/dev/null", captured.EnvironmentVariables["GIT_CONFIG_VALUE_0"]);
        Assert.Equal("core.fsmonitor", captured.EnvironmentVariables["GIT_CONFIG_KEY_1"]);
    }

    [Fact]
    public async Task TrustedSystemClient_ForwardsSshAgentSocketWithoutInheritingOtherEnvironment()
    {
        const string expectedSocket = "/private/tmp/powerforge-test-ssh-agent.sock";
        var previousSocket = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");
        ProcessRunRequest? captured = null;
        try
        {
            Environment.SetEnvironmentVariable("SSH_AUTH_SOCK", expectedSocket);
            var runner = new StubProcessRunner(request =>
            {
                captured = request;
                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            var client = GitClient.CreateTrustedSystemClient(runner, TimeSpan.FromSeconds(30));

            await client.RunRawAsync(Directory.GetCurrentDirectory(), ["status", "--short"]);

            Assert.NotNull(captured);
            Assert.False(captured!.InheritEnvironment);
            Assert.Equal(expectedSocket, captured.EnvironmentVariables!["SSH_AUTH_SOCK"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SSH_AUTH_SOCK", previousSocket);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public async Task IsolatedPathClient_PreservesPortableGitResolutionWithoutInheritedGitOverrides()
    {
        ProcessRunRequest? captured = null;
        var runner = new StubProcessRunner(request =>
        {
            captured = request;
            return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });

        await GitClient.CreateIsolatedPathClient(runner).RunRawAsync(
            Directory.GetCurrentDirectory(), ["rev-parse", "--verify", "HEAD^{commit}"]);

        Assert.NotNull(captured);
        Assert.Equal("git", captured!.FileName);
        Assert.False(captured.InheritEnvironment);
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), captured.EnvironmentVariables!["PATH"]);
        Assert.False(captured.EnvironmentVariables.ContainsKey("GIT_DIR"));
        Assert.False(captured.EnvironmentVariables.ContainsKey("GIT_WORK_TREE"));
        Assert.False(captured.EnvironmentVariables.ContainsKey("GIT_CONFIG_PARAMETERS"));
        Assert.False(captured.EnvironmentVariables.ContainsKey("GIT_CONFIG_NOSYSTEM"));
        Assert.False(captured.EnvironmentVariables.ContainsKey("GIT_CONFIG_GLOBAL"));
        Assert.Equal("0", captured.EnvironmentVariables["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("core.hooksPath", captured.EnvironmentVariables["GIT_CONFIG_KEY_0"]);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public async Task IsolatedPathClient_RespectsProtectedCheckoutTrustWithoutChangingExactAttestation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-protected-git-" + Guid.NewGuid().ToString("N"))).FullName;
        var checkout = Directory.CreateDirectory(Path.Combine(root, "checkout")).FullName;
        var home = Directory.CreateDirectory(Path.Combine(root, "home")).FullName;
        File.WriteAllText(Path.Combine(home, "system.gitconfig"), string.Empty);
        try
        {
            var setup = new GitClient();
            Assert.True((await setup.RunRawAsync(checkout, ["init", "--quiet"])).Succeeded);
            Assert.True((await setup.RunRawAsync(checkout,
                ["-c", "user.name=Release test", "-c", "user.email=release@example.test",
                 "-c", "commit.gpgsign=false", "-c", "core.hooksPath=" + (OperatingSystem.IsWindows() ? "NUL" : "/dev/null"),
                 "commit", "--allow-empty", "--quiet", "-m", "source"])).Succeeded);
            var expected = await setup.RunRawAsync(checkout, ["rev-parse", "HEAD"]);
            var runner = new ProtectedCheckoutRunner(home);
            var ordinary = GitClient.CreateIsolatedPathClient(runner);

            // Git's own ownership test switch avoids changing filesystem ownership
            // or any user configuration. Only this child process sees the switch.
            var untrusted = await ordinary.RunRawAsync(checkout, ["rev-parse", "HEAD"]);
            Assert.False(untrusted.Succeeded);
            Assert.Contains("dubious ownership", untrusted.StdErr, StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(Path.Combine(home, ".gitconfig"),
                "[safe]\n\tdirectory = \"" + checkout.Replace('\\', '/').Replace("\"", "\\\"") + "\"\n");

            var trusted = await ordinary.RunRawAsync(checkout, ["rev-parse", "HEAD"]);
            Assert.True(trusted.Succeeded, trusted.StdErr);
            Assert.Equal(expected.StdOut, trusted.StdOut);
            var exact = await GitClient.CreateTrustedSystemClient(runner).RunRawAsync(checkout, ["rev-parse", "HEAD"]);
            Assert.False(exact.Succeeded);
            Assert.Contains("dubious ownership", exact.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(root, true);
        }
    }

    private sealed class ProtectedCheckoutRunner(string home) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var environment = request.EnvironmentVariables!.ToDictionary(pair => pair.Key, pair => pair.Value);
            environment["HOME"] = home;
            environment["XDG_CONFIG_HOME"] = Path.Combine(home, ".config");
            environment["GIT_CONFIG_SYSTEM"] = Path.Combine(home, "system.gitconfig");
            environment["GIT_TEST_ASSUME_DIFFERENT_OWNER"] = "1";
            return new ProcessRunner().RunAsync(new ProcessRunRequest(
                request.FileName, request.WorkingDirectory, request.Arguments, request.Timeout,
                environment, request.CaptureOutput, request.CaptureError, inheritEnvironment: false), cancellationToken);
        }
    }
}
