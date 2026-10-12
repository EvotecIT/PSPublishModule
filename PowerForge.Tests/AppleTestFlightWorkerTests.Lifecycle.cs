using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleTestFlightWorkerTests
{
    [Theory]
    [InlineData((int)AppleTestFlightJobStage.Queued, true)]
    [InlineData((int)AppleTestFlightJobStage.WaitingForChecks, true)]
    [InlineData((int)AppleTestFlightJobStage.Planning, true)]
    [InlineData((int)AppleTestFlightJobStage.CheckpointReady, false)]
    [InlineData((int)AppleTestFlightJobStage.ReleaseReady, false)]
    [InlineData((int)AppleTestFlightJobStage.AppleProcessingPending, false)]
    [InlineData((int)AppleTestFlightJobStage.NeedsOperator, false)]
    public void OffBypassesOnlyUntouchedAutomaticIntent(int heldStageValue, bool bypass)
    {
        var heldStage = (AppleTestFlightJobStage)heldStageValue;
        var automatic = new AppleTestFlightJob { Automatic = true, Stage = heldStage };
        var manual = new AppleTestFlightJob { Stage = AppleTestFlightJobStage.ReleaseReady };
        var state = new AppleTestFlightWorkerState { Mode = AppleTestFlightAutomaticMode.Off, Jobs = [automatic, manual] };
        var selected = AppleTestFlightWorker.SelectRunnableJob(state);
        Assert.Same(bypass ? manual : heldStage == AppleTestFlightJobStage.NeedsOperator ? null : automatic, selected);
        // The same manual recovery candidate remains eligible behind a label-held
        // untouched intent, including on the pass after its successful plan.
        Assert.Same(manual, AppleTestFlightWorker.SelectManualBehindHeldIntent(state));
    }

    [Theory]
    [InlineData((int)AppleTestFlightJobStage.Queued, true)]
    [InlineData((int)AppleTestFlightJobStage.Planning, true)]
    [InlineData((int)AppleTestFlightJobStage.ReleaseReady, true)]
    [InlineData((int)AppleTestFlightJobStage.AppleProcessingPending, true)]
    [InlineData((int)AppleTestFlightJobStage.CheckpointExecuting, false)]
    [InlineData((int)AppleTestFlightJobStage.PublishingCheckpoint, false)]
    [InlineData((int)AppleTestFlightJobStage.Releasing, false)]
    public void ToolFailureRetriesOnlyAnAcknowledgedNonmutatingStage(int stageValue, bool retry)
    {
        var stage = (AppleTestFlightJobStage)stageValue;
        Assert.Equal(retry ? stage : AppleTestFlightJobStage.NeedsOperator,
            AppleTestFlightWorker.FailureStage(stage, new AppleTestFlightToolException("temporary GitHub or Git failure")));
        Assert.Equal(AppleTestFlightJobStage.NeedsOperator,
            AppleTestFlightWorker.FailureStage(stage, new InvalidOperationException("terminal source or policy validation")));
    }

    [Fact]
    public void PinnedEntryAdmitsOnlyStrictlyBehindWorkerSource_AndDoesNotMoveTheCheckout()
    {
        var root = Sandbox();
        try
        {
            var origin = Path.Combine(root, "origin.git");
            var consumer = Path.Combine(root, "consumer");
            RunTool("git", root, "init", "--bare", "--quiet", origin);
            RunTool("git", root, "clone", "--quiet", origin, consumer);
            RunTool("git", consumer, "checkout", "-b", "main");
            File.WriteAllText(Path.Combine(consumer, "source"), "one");
            Commit(consumer);
            RunTool("git", consumer, "push", "--quiet", "origin", "main");
            var first = RunTool("git", consumer, "rev-parse", "HEAD").Trim();
            File.WriteAllText(Path.Combine(consumer, "source"), "two");
            Commit(consumer);
            RunTool("git", consumer, "push", "--quiet", "origin", "main");
            // This exact synthetic reset models a clean dedicated checkout that
            // has not yet observed the next merge; no user checkout is involved.
            RunTool("git", consumer, "reset", "--hard", first);
            var harness = Path.Combine(root, "entry.ps1");
            File.WriteAllText(harness, """
                param($Owner, $Consumer, $First)
                $ErrorActionPreference='Stop'
                $script:gitPath=(Get-Command git -CommandType Application | Select-Object -First 1).Source
                $tokens=$null; $errors=$null
                $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $Owner 'scripts/Invoke-PinnedPowerForge.ps1'),[ref]$tokens,[ref]$errors)
                foreach($name in @('Invoke-GitText','Assert-UnlinkedPath','Assert-UnlinkedDirectory','Assert-CleanRepository')) {
                    $function=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
                    Invoke-Expression $function.Extent.Text
                }
                try { Assert-CleanRepository -Root $Consumer -Name Consumer -RequiredBranch main; throw 'Ordinary release accepted stale main.' }
                catch { if($_.Exception.Message -notlike '*does not match origin/main*'){throw} }
                $head=Assert-CleanRepository -Root $Consumer -Name Consumer -RequiredBranch main -AllowBehindMain
                if($head -ne $First){throw 'Control admission moved source.'}
                [IO.File]::WriteAllText((Join-Path $Consumer 'unexpected'), 'dirty')
                try { Assert-CleanRepository -Root $Consumer -Name Consumer -RequiredBranch main -AllowBehindMain; throw 'Worker accepted dirty source.' }
                catch { if($_.Exception.Message -notlike '*must be clean*'){throw} }
                Remove-Item -LiteralPath (Join-Path $Consumer 'unexpected')
                & $script:gitPath -C $Consumer -c core.hooksPath=/dev/null merge --ff-only refs/remotes/origin/main | Out-Null
                if($LASTEXITCODE -ne 0){throw 'Canonical fast-forward failed.'}
                Assert-CleanRepository -Root $Consumer -Name Consumer -RequiredBranch main | Out-Null
                [IO.File]::WriteAllText((Join-Path $Consumer 'source'), 'local divergence')
                & $script:gitPath -C $Consumer add source
                & $script:gitPath -C $Consumer -c user.name=Synthetic -c user.email=synthetic@example.invalid -c commit.gpgSign=false -c core.hooksPath=/dev/null commit -qm local
                try { Assert-CleanRepository -Root $Consumer -Name Consumer -RequiredBranch main -AllowBehindMain; throw 'Worker accepted ahead source.' }
                catch { if($_.Exception.Message -notlike 'Git failed*'){throw} }
                'PASS pinned behind-main lifecycle'
                """);
            Assert.Contains("PASS pinned behind-main lifecycle", RunTool("pwsh", root, "-NoProfile", "-File", harness,
                "-Owner", FindRepository(), "-Consumer", consumer, "-First", first));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ActiveWriterDoesNotBlockCliStatusOrPauseAcknowledgement()
    {
        var root = Sandbox();
        try
        {
            var policy = new AppleTestFlightWorkerPolicy
            {
                Repository = "synthetic/app", ConsumerRoot = root, OwnerRoot = FindRepository(), OwnerCommit = new string('a', 40),
                StateRoot = root, ScratchRoot = root, BaselineSourceCommit = new string('b', 40), AcceptRequestsAfter = DateTimeOffset.UtcNow.AddDays(-1),
                RequiredChecks = ["Synthetic check"], PublisherPaths = ["project.yml"], BetaGroupId = Guid.NewGuid().ToString(),
                Targets = [new() { Name = "App", BundleId = "com.example.synthetic-active", AppId = "123" }]
            };
            // Durable state must be outside the checkout, even in this synthetic process proof.
            policy.ConsumerRoot = Directory.CreateDirectory(Path.Combine(root, "consumer")).FullName;
            var directory = Directory.CreateDirectory(Path.Combine(root, "queues", AppleReleaseAppState.Hash(policy.Repository))).FullName;
            AppleReleaseAppState.AtomicWrite(Path.Combine(directory, "queue.json"), new AppleTestFlightWorkerState
            { Repository = policy.Repository, Jobs = [new() { Id = "active", Stage = AppleTestFlightJobStage.Releasing }] });
            var path = Path.Combine(root, "policy.json");
            File.WriteAllText(path, JsonSerializer.Serialize(policy, AppleTestFlightWorker.Json));
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var cli = Path.Combine(FindRepository(), "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll");
            Assert.True(File.Exists(cli));
            using var writer = new FileStream(Path.Combine(directory, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            foreach (var operation in new[] { "status", "pause", "resume" })
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { WorkingDirectory = root,
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                // testhost is not dotnet; use the configured runtime through PATH.
                start.FileName = "dotnet";
                foreach (var value in new[] { cli, "apple-testflight", operation, "--policy", path, "--config", policy.ConfigPath, "--output", "json" })
                    start.ArgumentList.Add(value);
                start.Environment["POWERFORGE_APPLE_STATE_ROOT"] = root;
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                Assert.True(process.WaitForExit(30000));
                Assert.True(process.ExitCode == 0, output + error);
                using var envelope = JsonDocument.Parse(output);
                Assert.Equal(operation == "pause", envelope.RootElement.GetProperty("result").GetProperty("Paused").GetBoolean());
                Assert.Equal("Releasing", envelope.RootElement.GetProperty("result").GetProperty("Jobs")[0].GetProperty("Stage").GetString());
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Commit(string root)
    {
        RunTool("git", root, "add", ".");
        RunTool("git", root, "-c", "user.name=Synthetic", "-c", "user.email=synthetic@example.invalid", "-c", "commit.gpgSign=false",
            "-c", "core.hooksPath=/dev/null", "commit", "--quiet", "-m", "Synthetic source");
    }

    private static string RunTool(string tool, string root, params string[] arguments)
    {
        var start = new ProcessStartInfo(tool) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30000));
        Assert.True(process.ExitCode == 0, output + error);
        return output;
    }
}
