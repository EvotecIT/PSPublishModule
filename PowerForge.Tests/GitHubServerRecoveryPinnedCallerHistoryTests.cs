namespace PowerForge.Tests;

public sealed partial class GitHubServerRecoveryValidationSecurityTests
{
    [Fact]
    public void Validator_RetrievesCallerPinFromDeletedBranch()
    {
        var result = RunValidator(unadvertisedCallerCommit: true, includeCaptureStartup: true);

        result.EnsureSuccess();
    }

    [Fact]
    public void Validator_RejectsChangedSourceAfterRetrievingCallerPin()
    {
        var result = RunValidator(
            unadvertisedCallerCommit: true, includeCaptureStartup: true, changeSourceAfterCallerPin: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("differs from its pinned repository commit", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("retrieved")]
    [InlineData("present")]
    [InlineData("wrong-origin")]
    [InlineData("unavailable")]
    public void CallerPinRetrieval_PreservesCheckoutAndFailsClosed(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-caller-history-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "caller");
        Directory.CreateDirectory(workspace);
        try
        {
            File.WriteAllText(Path.Combine(workspace, "source.txt"), "pinned source\n");
            InitializeGitRepository(workspace);
            var pin = PrepareUnadvertisedCallerCommit(root, workspace);
            var head = RunProcess("git", workspace, "rev-parse", "HEAD").StandardOutput.Trim();
            var source = File.ReadAllText(Path.Combine(workspace, "source.txt"));
            var fetchHeadPath = Path.Combine(workspace, ".git", "FETCH_HEAD");
            File.WriteAllText(fetchHeadPath, "preserve existing fetch receipt\n");
            if (scenario == "present")
            {
                pin = head;
                RunProcess("git", workspace, "remote", "remove", "origin").EnsureSuccess();
            }
            if (scenario == "wrong-origin")
                RunProcess("git", workspace, "remote", "set-url", "origin", "https://github.com/Other/Repository.git").EnsureSuccess();
            if (scenario == "unavailable")
                pin = new string('f', 40);

            var wrapper = Path.Combine(root, "retrieve.ps1");
            File.WriteAllText(wrapper, """
                param([string] $Helper, [string] $Workspace, [string] $Pin)
                $ErrorActionPreference = 'Stop'
                $PSNativeCommandUseErrorActionPreference = $false
                . $Helper
                $env:GIT_TERMINAL_PROMPT = 'original-prompt'
                $env:GCM_INTERACTIVE = 'original-interaction'
                $failure = $null
                try {
                    Assert-PowerForgeCallerGitCommit -Workspace $Workspace -CallerRepository 'EvotecIT/ExampleSite' -RepositoryRef $Pin
                } catch { $failure = $_.Exception.Message }
                if ($env:GIT_TERMINAL_PROMPT -ne 'original-prompt' -or $env:GCM_INTERACTIVE -ne 'original-interaction') {
                    throw 'Caller Git prompt environment was not restored.'
                }
                if ($failure) { [Console]::Error.WriteLine($failure); exit 1 }
                """);
            var result = RunProcess("pwsh", root, "-NoLogo", "-NoProfile", "-File", wrapper,
                "-Helper", GetRepoPath(".github", "actions", "powerforge-server-recovery-validate", "Assert-PowerForgeCallerGitCommit.ps1"),
                "-Workspace", workspace, "-Pin", pin);

            Assert.Equal(head, RunProcess("git", workspace, "rev-parse", "HEAD").StandardOutput.Trim());
            Assert.Equal(source, File.ReadAllText(Path.Combine(workspace, "source.txt")));
            Assert.Equal("preserve existing fetch receipt\n", File.ReadAllText(fetchHeadPath));
            if (scenario is "retrieved" or "present")
            {
                result.EnsureSuccess();
                RunProcess("git", workspace, "cat-file", "-e", pin + "^{commit}").EnsureSuccess();
            }
            else
            {
                Assert.NotEqual(0, result.ExitCode);
                Assert.Contains(scenario == "wrong-origin" ? "different repository origin" : "Unable to retrieve the exact pinned", result.AllOutput, StringComparison.Ordinal);
                Assert.NotEqual(0, RunProcess("git", workspace, "cat-file", "-e", pin + "^{commit}").ExitCode);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort test cleanup */ }
        }
    }

    private static string PrepareUnadvertisedCallerCommit(string root, string workspace)
    {
        var tree = RunProcess("git", workspace, "rev-parse", "HEAD^{tree}").StandardOutput.Trim();
        var orphan = RunProcess("git", workspace, "-c", "commit.gpgsign=false", "commit-tree", tree,
            "-m", "Historical recovery pin");
        orphan.EnsureSuccess();
        var pin = orphan.StandardOutput.Trim();
        var origin = Path.Combine(root, "origin.git");
        RunProcess("git", root, "init", "--bare", "--initial-branch=main", origin).EnsureSuccess();
        RunProcess("git", workspace, "push", origin, "HEAD:refs/heads/main", pin + ":refs/heads/deleted-recovery").EnsureSuccess();
        RunProcess("git", workspace, "push", origin, ":refs/heads/deleted-recovery").EnsureSuccess();
        Directory.Move(workspace, Path.Combine(root, "original-caller"));
        RunProcess("git", root, "-c", "core.autocrlf=false", "clone", "--no-local", "--branch", "main", origin, workspace).EnsureSuccess();
        Assert.NotEqual(0, RunProcess("git", workspace, "cat-file", "-e", pin + "^{commit}").ExitCode);
        RunProcess("git", workspace, "remote", "set-url", "origin", "https://github.com/EvotecIT/ExampleSite.git").EnsureSuccess();
        RunProcess("git", workspace, "config", "url." + new Uri(origin + Path.DirectorySeparatorChar).AbsoluteUri + ".insteadOf",
            "https://github.com/EvotecIT/ExampleSite.git").EnsureSuccess();
        return pin;
    }
}
