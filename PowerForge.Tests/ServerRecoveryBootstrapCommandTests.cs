using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerRecoveryBootstrapCommandTests
{
    [Fact]
    public void ApplyProgram_PreservesPlannerCommandsWithoutTheOperatorOnlyTerminalStep()
    {
        var manifest = VerifiedManifest();
        var plan = WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        Assert.True(plan[^1].Manual);
        var program = WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest);
        Assert.Contains("set -Eeuo pipefail", program);
        Assert.DoesNotContain("Manual bootstrap step required", program);
        Assert.Contains(plan[0].Command!, program);
    }

    [Fact]
    public void ApplyProgram_RejectsMissingOrOnlyOptionalVerification()
    {
        var manifest = new PowerForgeServerRecoveryManifest();
        Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest));
        manifest.Verify = new PowerForgeServerVerify { Commands = [new() { Id = "optional", Command = "true", Required = false }] };
        Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest));
    }

    [Fact]
    public void ApplyProgram_RejectsSensitiveBootstrapBeforeAnyCommandsCanRun()
    {
        var manifest = VerifiedManifest();
        manifest.Bootstrap = new PowerForgeServerCommandGroup { Commands = [new() { Id = "secret", Command = "true", Sensitive = true }] };
        var error = Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest));
        Assert.Contains("unresolved", error.Message);
    }

    [Fact]
    public void ApplyProgram_RejectsSkippedSensitiveVerification()
    {
        var manifest = VerifiedManifest();
        manifest.Verify!.Commands![0].Sensitive = true;
        Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://name:password@example.test/")]
    public void ApplyProgram_RejectsInvalidVerificationUrlsBeforeExecution(string? address)
    {
        var manifest = VerifiedManifest();
        manifest.Verify = new() { Urls = [new() { Url = address }] };
        Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest));
    }

    [Fact]
    public void StepFailure_StopsAndListAndWorkingDirectoryFailuresOnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;
        foreach (var failingCommand in new[] { "false && printf unexpected", "cd /no-such-powerforge-directory && printf unexpected" })
        {
            var script = WebCliCommandHandlers.RenderBootstrapPlanScript([
                new() { Order = 1, Command = failingCommand }, new() { Order = 2, Command = "printf mutation" }
            ]);
            var result = WebCliCommandHandlers.RunLocalServerScript(script);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Empty(result.Stdout);
        }
    }

    [Fact]
    public void BootstrapWorkingDirectory_IsDeclaredAndDoesNotAffectNextStepOnLinux()
    {
        var manifest = VerifiedManifest();
        manifest.Bootstrap = new() { Commands = [new() { Id = "cwd", Command = "pwd", WorkingDirectory = "/" }, new() { Id = "next", Command = "pwd" }] };
        var steps = WebCliCommandHandlers.BuildBootstrapPlanSteps(manifest, []);
        Assert.Contains("cd -- '/'", steps.Single(step => step.Title == "cwd").Command);
        if (!OperatingSystem.IsLinux()) return;
        var result = WebCliCommandHandlers.RunLocalServerScript(WebCliCommandHandlers.BuildExecutableBootstrapScript(manifest));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("/\n" + Directory.GetCurrentDirectory() + "\n", result.Stdout);
    }

    [Fact]
    public void LocalProgram_PreservesOrderingStdinIsolationAndFailureOnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;
        var result = WebCliCommandHandlers.RunLocalServerScript("set -Eeuo pipefail\nprintf 'first\\n'\nread -r ignored || true\nprintf 'last\\n'\n");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("first\nlast\n", result.Stdout);
        result = WebCliCommandHandlers.RunLocalServerScript("set -Eeuo pipefail\nexit 29\nprintf 'unreachable'\n");
        Assert.Equal(29, result.ExitCode);
        Assert.Empty(result.Stdout);
    }

    [Fact]
    public void ManifestGuard_RejectsWritableAncestryOnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;
        var path = Path.Combine(Path.GetTempPath(), "powerforge-bootstrap-manifest-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "{}");
        try
        {
            var result = WebCliCommandHandlers.RunLocalServerScript(WebCliCommandHandlers.BuildBootstrapManifestGuard(path));
            Assert.NotEqual(0, result.ExitCode); // /tmp is writable, even for a root-owned file.
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LocalProgram_DrainsLargeOutputWithABoundedPreviewOnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;
        var result = WebCliCommandHandlers.RunLocalServerScript("head -c 131072 /dev/zero | tr '\\000' a");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(65536, result.Stdout.Length);
        Assert.All(result.Stdout, character => Assert.Equal('a', character));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_OnRootLinuxRunsBootstrapThenLocalVerification(bool failVerification)
    {
        if (!OperatingSystem.IsLinux() || WebCliCommandHandlers.RunLocalServerScript("id -u").Stdout.Trim() != "0") return;
        var root = "/root/powerforge-bootstrap-test-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        try
        {
            var manifest = VerifiedManifest();
            manifest.SchemaVersion = 2;
            manifest.Target = new() { Host = "no-such-host.invalid" };
            manifest.Bootstrap = new() { Commands = [new() { Id = "marker", Command = $"printf ready > '{root}/marker'", Required = true }] };
            manifest.Verify!.Commands = [new() { Id = "verify", Command = failVerification ? "exit 37" : "test -s marker", WorkingDirectory = root, Required = true }];
            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, WebCliJson.Options));

            var exitCode = WebCliCommandHandlers.HandleServer(["bootstrap", "--manifest", manifestPath, "--apply"], false, new(), 1);

            Assert.Equal(failVerification ? 1 : 0, exitCode);
            Assert.Equal("ready", File.ReadAllText(Path.Combine(root, "marker")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BootstrapWithoutApply_OnlyWritesReviewPlan()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-bootstrap-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifestPath, """{"schemaVersion":2,"target":{"host":"no-such-host.invalid"},"bootstrap":{"commands":[{"id":"must-not-run","command":"exit 71","required":true}]}}""");
            Assert.Equal(0, WebCliCommandHandlers.HandleServer(["bootstrap", "--manifest", manifestPath, "--out", Path.Combine(root, "plan")], false, new(), 1));
            Assert.True(File.Exists(Path.Combine(root, "plan", "bootstrap-plan.sh")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static PowerForgeServerRecoveryManifest VerifiedManifest() => new()
    {
        Verify = new PowerForgeServerVerify { Commands = [new() { Id = "ready", Command = "true", Required = true }] }
    };
}
