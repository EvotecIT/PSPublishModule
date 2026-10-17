using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public async Task Execute_AppleShipCheckpoint_RestartAdmitsOnlyCanonicalCompletedPublisherDiff()
    {
        if (!File.Exists("/usr/local/bin/pwsh") || !File.Exists("/usr/bin/git")) return;
        var root = CreateSandbox();
        try
        {
            var consumer = Directory.CreateDirectory(Path.Combine(root, "consumer")).FullName;
            CreateXcodeProject(consumer, "CasaRay.xcodeproj", "1.5.0", "13");
            WriteXcodeGenVersionSource(consumer, "1.5.0", "13");
            AppleDeploymentTestFixture.WriteSharedSchemes(consumer, "CasaRay");
            var key = Path.Combine(consumer, "AuthKey_TEST.p8");
            File.WriteAllText(key, "synthetic-private-key");
            var spec = CreateAppleAutomationSpec(consumer, key);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            var group = Guid.NewGuid().ToString();
            spec.AppleApps.TestFlightBetaGroupIds = [group];
            EnableInternalAppleShipTargets(spec);
            var config = Path.Combine(consumer, "powerforge.release.json");
            File.WriteAllText(config, JsonSerializer.Serialize(spec));
            var source = CommitAppleShipSource(consumer);
            RunSnapshotGit(consumer, "remote", "add", "origin", "https://github.com/synthetic/app.git");
            RunSnapshotGit(consumer, "branch", "-M", "main");
            RunSnapshotGit(consumer, "update-ref", "refs/remotes/origin/main", source);
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"),
                generateAppleProject: _ => true, getHighestAppleBuildNumber: (_, _, _) => 13);
            var preview = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = config, PlanOnly = true, AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0", AppleSourceCommit = source, AppleShipTestFlightTargets = ["CasaRay iOS"]
            });
            var completed = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = config, AppleAction = PowerForgeAppleReleaseAction.Ship, AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = source, AppleShipTestFlightTargets = ["CasaRay iOS"],
                AppleExpectedPlanSha256 = preview.AppleReceipt!.PlanSha256, AppleActionConfirmed = true
            });
            Assert.True(completed.Success, completed.ErrorMessage);
            Assert.Equal(PowerForgeAppleShipPhase.VersionCheckpoint, completed.AppleReceipt!.ShipPhase);
            var stateRoot = Directory.CreateDirectory(Path.Combine(root, "state")).FullName;
            var queue = Directory.CreateDirectory(Path.Combine(stateRoot, "queues", AppleReleaseAppState.Hash("synthetic/app"))).FullName;
            var job = new AppleTestFlightJob
            {
                Id = "checkpoint", Stage = AppleTestFlightJobStage.CheckpointReady, Source = source, OriginalSource = source,
                PlanSha256 = preview.AppleReceipt.PlanSha256, Version = completed.AppleReceipt.Versioning!.MarketingVersion,
                Build = completed.AppleReceipt.Versioning.BuildNumber, UpdatedAt = DateTimeOffset.UtcNow
            };
            void Save() => AppleReleaseAppState.AtomicWrite(Path.Combine(queue, "queue.json"), new AppleTestFlightWorkerState
                { Repository = "synthetic/app", Jobs = [job] });
            Save();
            var owner = FindWorkerOwner();
            var policy = new AppleTestFlightWorkerPolicy
            {
                Repository = "synthetic/app", ConsumerRoot = consumer, OwnerRoot = owner, OwnerCommit = new string('A', 40),
                StateRoot = stateRoot, ScratchRoot = root, AcceptRequestsAfter = DateTimeOffset.UtcNow.AddDays(-1),
                BaselineSourceCommit = source.ToUpperInvariant(), RequiredChecks = ["Synthetic check"], BetaGroupId = group,
                Targets = [new() { Name = "CasaRay iOS", BundleId = "com.evotecit.casaray", AppId = "6778025328", Platform = ApplePlatform.iOS }],
                PublisherPaths = ["project.yml", "CasaRay.xcodeproj/project.pbxproj"]
            };
            var policyPath = Path.Combine(root, "policy.json");
            File.WriteAllText(policyPath, JsonSerializer.Serialize(policy, AppleTestFlightWorker.Json));
            (int ExitCode, string Output, string Error) Evidence() => AppleTestFlightWorkerTests.RunCli(root,
                "apple-testflight", "checkpoint-evidence", "--policy", policyPath, "--config", policy.ConfigPath, "--output", "json");
            var admitted = Evidence();
            Assert.True(admitted.ExitCode == 0, admitted.Output + admitted.Error);
            using (var envelope = JsonDocument.Parse(admitted.Output))
                Assert.Contains(Path.Combine(consumer, "project.yml"), envelope.RootElement.GetProperty("result").EnumerateArray().Select(value => value.GetString()));

            var harness = Path.Combine(root, "admission.ps1");
            File.WriteAllText(harness, """
                param($Owner,$Consumer,$Policy,$Cli,$State,$Source)
                $ErrorActionPreference='Stop'
                $consumer=$Consumer
                $script:gitPath='/usr/bin/git'
                $env:POWERFORGE_APPLE_STATE_ROOT=$State
                $script:validatedCredentialEnvironment=@{}
                $script:allowedConsumerEvidencePaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $ArgumentList=@('apple-testflight','run','--policy',$Policy,'--config','powerforge.release.json')
                $tokens=$null; $errors=$null
                $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $Owner 'scripts/Invoke-PinnedPowerForge.ps1'),[ref]$tokens,[ref]$errors)
                foreach($name in @('Get-OptionValue','Get-RedactedToolText','Invoke-RedactedProcess','Assert-UnlinkedPath')) {
                    $function=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
                    Invoke-Expression $function.Extent.Text
                }
                . (Join-Path $Owner 'scripts/Invoke-PinnedPowerForge.Evidence.ps1')
                . (Join-Path $Owner 'scripts/Invoke-PinnedPowerForge.Recovery.ps1')
                $validator=Join-Path $Owner '.github/actions/apple-release/Assert-TrackedAppleReleaseInputs.ps1'
                try { & $validator -ConfigPath (Join-Path $Consumer 'powerforge.release.json') -SourceCommit $Source -SkipToolManifest; throw 'Dirty checkpoint was admitted without canonical evidence.' }
                catch { if($_.Exception.Message -notlike '*differs from the exact source commit*'){throw} }
                Register-AppleWorkerCheckpointEvidence -DotNetPath (Get-Command dotnet).Source -CliAssembly $Cli -NuGetPackagesPath ([IO.Path]::GetTempPath()) -OutputPath (Join-Path (Split-Path $Consumer) 'evidence.json')
                & $validator -ConfigPath (Join-Path $Consumer 'powerforge.release.json') -SourceCommit $Source -SkipToolManifest -PublisherCheckpointPaths $script:publisherCheckpointPaths
                """ );
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var configuredCli = Environment.GetEnvironmentVariable(PowerForgeCliTestHost.AssemblyPathEnvironmentVariable);
            var cli = configuredCli is null
                ? Path.Combine(owner, "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll")
                : PowerForgeCliTestHost.ResolveAssemblyPath(owner, configuredCli);
            var start = new ProcessStartInfo("/usr/local/bin/pwsh") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-File", harness, "-Owner", owner, "-Consumer", consumer,
                "-Policy", policyPath, "-Cli", cli, "-State", stateRoot, "-Source", source }) start.ArgumentList.Add(value);
            using (var process = Process.Start(start)!)
            {
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await process.WaitForExitAsync(timeout.Token);
                Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
            }
            var bytes = File.ReadAllText(Path.Combine(consumer, "project.yml"));
            File.AppendAllText(Path.Combine(consumer, "project.yml"), "behavior: changed\n");
            Assert.NotEqual(0, Evidence().ExitCode);
            File.WriteAllText(Path.Combine(consumer, "project.yml"), bytes.Replace("14", "15"));
            Assert.NotEqual(0, Evidence().ExitCode);
            File.WriteAllText(Path.Combine(consumer, "project.yml"), bytes);
            job.PlanSha256 = new string('b', 64); Save();
            Assert.NotEqual(0, Evidence().ExitCode);
            job.PlanSha256 = preview.AppleReceipt.PlanSha256; Save();
            var changedMain = RunSnapshotGit(consumer, "commit-tree", "HEAD^{tree}", "-p", source, "-m", "Upstream moved").Trim();
            RunSnapshotGit(consumer, "update-ref", "refs/remotes/origin/main", changedMain);
            Assert.NotEqual(0, Evidence().ExitCode);
            RunSnapshotGit(consumer, "update-ref", "refs/remotes/origin/main", source);
            RunSnapshotGit(consumer, "add", "project.yml");
            Assert.NotEqual(0, Evidence().ExitCode);
            RunSnapshotGit(consumer, "reset", "--quiet", "HEAD", "--", "project.yml");
            var receiptPath = completed.AppleAppPlan!.ReceiptPath;
            var receiptBytes = File.ReadAllBytes(receiptPath);
            var tampered = completed.AppleReceipt;
            tampered.ErrorMessage = "Changed outside canonical receipt writer";
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(tampered, AppleTestFlightWorker.Json));
            Assert.NotEqual(0, Evidence().ExitCode);
            File.WriteAllBytes(receiptPath, receiptBytes);
            job.Stage = AppleTestFlightJobStage.Releasing; Save();
            var held = Evidence();
            Assert.Equal(0, held.ExitCode);
            using var heldEnvelope = JsonDocument.Parse(held.Output);
            Assert.Empty(heldEnvelope.RootElement.GetProperty("result").EnumerateArray());
        }
        finally { TryDelete(root); }
    }

    private static string FindWorkerOwner()
        => PowerForgeCliTestHost.FindRepositoryRoot();
}
