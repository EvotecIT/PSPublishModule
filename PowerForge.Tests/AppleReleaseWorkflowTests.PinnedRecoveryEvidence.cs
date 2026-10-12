namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Theory]
    [MemberData(nameof(PinnedRecoveryCases))]
    public void PinnedAppleRecoveryAdmitsOnlyReceiptBoundOutputs(string scenario, string expectedPhase)
    {
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-recovery-evidence-{Guid.NewGuid():N}");
        var consumer = Path.Combine(parent, "consumer");
        IDisposable? activeLease = null;
        try
        {
            Directory.CreateDirectory(consumer);
            var project = Directory.CreateDirectory(Path.Combine(consumer, "Sample.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"),
                "{ MARKETING_VERSION = 1.0; CURRENT_PROJECT_VERSION = 7; }");
            File.WriteAllText(Path.Combine(consumer, ".gitignore"), "build/\n");
            File.WriteAllText(Path.Combine(consumer, "powerforge.release.json"),
                """
                {
                  "AppleApps": {
                    "ProjectRoot": ".",
                    "ArchiveRoot": "build/powerforge/apple/archives",
                    "ExportRoot": "build/powerforge/apple/export",
                    "Apps": [ {
                      "Name": "Sample", "ProjectPath": "Sample.xcodeproj", "Scheme": "Sample",
                      "BundleId": "com.example.sample", "Platform": "iOS", "DistributionRoute": "AppStore",
                      "AppStoreConnectAppId": "123", "MarketingVersion": "1.0", "BuildNumber": "7"
                    } ]
                  }
                }
                """);
            Run("git", consumer, "init", "--quiet").EnsureSuccess();
            Run("git", consumer, "add", ".").EnsureSuccess();
            CommitTrackedReleaseSandbox(consumer, "Tracked recovery source");
            var source = Run("git", consumer, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();
            var output = Path.Combine(consumer, "build", "powerforge", "apple");
            var archive = Directory.CreateDirectory(Path.Combine(output, "archives", "iOS", "Sample.xcarchive"));
            var payload = Path.Combine(archive.FullName, "archive.bin");
            File.WriteAllText(payload, "signed archive fixture");
            if (scenario == "internal-framework-links")
            {
                var framework = Path.Combine(archive.FullName, "Products", "Library", "Frameworks", "Sample.framework");
                var version = Directory.CreateDirectory(Path.Combine(framework, "Versions", "A"));
                var resources = Directory.CreateDirectory(Path.Combine(version.FullName, "Resources"));
                File.WriteAllText(Path.Combine(resources.FullName, "Info.plist"), "framework resources");
                File.WriteAllText(Path.Combine(version.FullName, "Sample"), "framework executable");
                Directory.CreateSymbolicLink(Path.Combine(framework, "Versions", "Current"), "A");
                Directory.CreateSymbolicLink(Path.Combine(framework, "Resources"), "Versions/Current/Resources");
                File.CreateSymbolicLink(Path.Combine(framework, "Sample"), "Versions/Current/Sample");
            }
            var plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = consumer,
                SourceCommit = source,
                ReceiptPath = Path.Combine(output, "release-receipt.json"),
                ReceiptHistoryPath = Path.Combine(output, "receipts")
            };
            var target = new PowerForgeAppleReleaseTargetReceipt
            {
                Name = "Sample", BundleId = "com.example.sample", Platform = ApplePlatform.iOS,
                ProjectPath = "Sample.xcodeproj", Scheme = "Sample", Configuration = "Release",
                DistributionRoute = AppleDistributionRoute.AppStore, Destination = "generic/platform=iOS",
                AppId = "123", Version = "1.0", Build = "7", ArchiveCreated = true,
                ArchivePath = Path.GetRelativePath(consumer, archive.FullName).Replace('\\', '/'),
                ArchiveSha256 = AppleNotarizationService.ComputeArtifactSha256(archive.FullName)
            };
            if (scenario == "escaped-archive")
            {
                var outside = Directory.CreateDirectory(Path.Combine(parent, "outside.xcarchive"));
                File.WriteAllText(Path.Combine(outside.FullName, "archive.bin"), "outside fixture");
                target.ArchivePath = "../outside.xcarchive";
                target.ArchiveSha256 = AppleNotarizationService.ComputeArtifactSha256(outside.FullName);
            }
            new AppleReleaseReceiptStore().WriteAttempt(plan, new PowerForgeAppleReleaseReceipt
            {
                Action = PowerForgeAppleReleaseAction.Archive,
                OperationPhase = "Completed",
                SourceCommit = source,
                Success = true,
                Targets = [target]
            });
            var planPath = Path.Combine(output, "release-plan.json");
            byte[]? previousPlan = null;
            if (scenario == "historical-worker-replan")
            {
                new AppleReleaseReceiptStore().WritePlan(consumer, planPath, new PowerForgeAppleReleaseReceipt
                { Action = PowerForgeAppleReleaseAction.Ship, PlanOnly = true, SourceCommit = source, PlanSha256 = new string('a', 64) });
                previousPlan = File.ReadAllBytes(planPath);
                File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"),
                    "{ MARKETING_VERSION = 1.0; CURRENT_PROJECT_VERSION = 8; }");
                Run("git", consumer, "add", ".").EnsureSuccess();
                CommitTrackedReleaseSandbox(consumer, "Publisher checkpoint");
                source = Run("git", consumer, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();
            }
            switch (scenario)
            {
                case "nonancestor-archive":
                    Run("git", consumer, "checkout", "--orphan", "unrelated").EnsureSuccess();
                    CommitTrackedReleaseSandbox(consumer, "Unrelated source");
                    source = Run("git", consumer, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();
                    break;
                case "altered-archive":
                    File.WriteAllText(payload, "changed after receipt");
                    break;
                case "linked-archive":
                    var retained = Path.Combine(parent, "linked-target.xcarchive");
                    Directory.Move(archive.FullName, retained);
                    Directory.CreateSymbolicLink(archive.FullName, retained);
                    break;
                case "active-journal-lock":
                    activeLease = AppleReleaseReceiptJournalLease.Acquire(plan);
                    break;
                case "nonempty-journal-lock":
                    File.WriteAllText(AppleReleaseReceiptJournalLease.CreateLockPath(plan.ReceiptPath), "unexpected content");
                    break;
            }

            var harness = Path.Combine(parent, "recovery-evidence-harness.ps1");
            File.WriteAllText(harness, PinnedRecoveryHarness);
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var cli = Path.Combine(root, "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll");
            Assert.True(File.Exists(cli), $"PowerForge CLI test dependency was not built: {cli}");
            if (previousPlan is not null)
            {
                Run("pwsh", parent, "-NoLogo", "-NoProfile", "-File", harness,
                    "-Owner", root, "-Consumer", consumer, "-Cli", cli, "-Source", source, "-WorkerOnly").EnsureSuccess();
                var retained = Path.Combine(parent, "retained-plans");
                AppleTestFlightWorker.RetainPreviousSourcePlan(consumer, planPath, source, retained);
                Assert.False(File.Exists(planPath));
                Assert.Equal(previousPlan, File.ReadAllBytes(Directory.GetFiles(retained).Single()));
                // This is the same canonical writer used by Ship --plan. The
                // actual admission/recovery subprocess must accept the new source
                // while preserving and hashing the prior source's archive.
                new AppleReleaseReceiptStore().WritePlan(consumer, planPath, new PowerForgeAppleReleaseReceipt
                { Action = PowerForgeAppleReleaseAction.Ship, PlanOnly = true, SourceCommit = source, PlanSha256 = new string('b', 64) });
            }
            var result = Run("pwsh", parent, "-NoLogo", "-NoProfile", "-File", harness,
                "-Owner", root, "-Consumer", consumer, "-Cli", cli, "-Source", source,
                "-ExpectedPhase", expectedPhase);
            result.EnsureSuccess();
            Assert.Contains(expectedPhase.Length == 0 ? "PASS clean recovery and unrelated-content rejection" : "PASS rejected recovery evidence",
                result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            activeLease?.Dispose();
            DeletePinnedEvidenceDirectory(parent);
        }
    }

    public static IEnumerable<object[]> PinnedRecoveryCases()
    {
        yield return ["valid", ""];
        yield return ["historical-worker-replan", ""];
        yield return ["nonancestor-archive", "ArchiveValidation"];
        yield return ["altered-archive", "ArchiveValidation"];
        yield return ["escaped-archive", "ArchiveValidation"];
        yield return ["active-journal-lock", "JournalLockValidation"];
        yield return ["nonempty-journal-lock", "JournalLockValidation"];
        if (!OperatingSystem.IsWindows())
        {
            yield return ["linked-archive", "ArchiveValidation"];
            yield return ["internal-framework-links", ""];
        }
    }

    private const string PinnedRecoveryHarness = """
        param([string] $Owner, [string] $Consumer, [string] $Cli, [string] $Source, [string] $ExpectedPhase, [switch] $WorkerOnly)
        $ErrorActionPreference = 'Stop'
        $consumer = [IO.Path]::GetFullPath($Consumer)
        $script:gitPath = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $script:validatedCredentialEnvironment = @{}
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $Owner 'scripts/Invoke-PinnedPowerForge.ps1'), [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) { throw 'Pinned helper contains parse errors.' }
        foreach ($name in @('Invoke-GitText', 'Assert-UnlinkedPath', 'Assert-UnlinkedDirectory',
                            'Resolve-OptionPath', 'Resolve-PathFromBase', 'Get-OptionValue',
                            'Get-RedactedToolText', 'Invoke-RedactedProcess')) {
            $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
            if ($null -eq $function) { throw "Pinned helper function was not found: $name" }
            Invoke-Expression $function.Extent.Text
        }
        . (Join-Path $Owner 'scripts/Invoke-PinnedPowerForge.Evidence.ps1')
        . (Join-Path $Owner 'scripts/Invoke-PinnedPowerForge.Recovery.ps1')
        $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $consumerHead = $Source
        $cliAssembly = $Cli
        $nugetPackages = Join-Path $PSScriptRoot 'nuget'
        $temporaryRoot = $PSScriptRoot
        $admissionCommands = $ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -in @('Register-AppleAutomationEvidence', 'Register-AppleRecoveryEvidence', 'Assert-ConsumerRepositoryContent')
        }, $true)
        if($ExpectedPhase -eq 'JournalLockValidation') {
            foreach($control in @('status','pause','resume')) {
                $script:allowedConsumerEvidencePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $ArgumentList=@('apple-testflight',$control,'--config','powerforge.release.json')
                foreach($command in $admissionCommands) { Invoke-Expression $command.Extent.Text }
            }
        }
        if($WorkerOnly) { $commands = ,@('apple-testflight','run') } else {
            $commands = @(@('apple-release','Status'), @('apple-release','Upload'), @('apple-testflight','run'),
              @('apple-testflight','status'), @('apple-testflight','pause'), @('apple-testflight','resume'))
        }
        foreach ($commandArguments in $commands) {
            $script:allowedConsumerEvidencePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $ArgumentList = @($commandArguments) + @('--config', 'powerforge.release.json')
            try {
                foreach ($command in $admissionCommands) { Invoke-Expression $command.Extent.Text }
            } catch {
                if ([string]::IsNullOrWhiteSpace($ExpectedPhase)) { throw }
                if ($_.Exception.Message -notlike 'Canonical Apple recovery evidence validation failed*') { throw }
                $rejected = Get-Content -LiteralPath (Join-Path $temporaryRoot 'apple-recovery-evidence.json') -Raw | ConvertFrom-Json
                if ($rejected.success -ne $false -or $rejected.result.rejectionPhase -ne $ExpectedPhase) {
                    throw "Recovery rejected at an unexpected boundary: $($rejected.result.rejectionPhase)"
                }
                'PASS rejected recovery evidence'
                exit 0
            }
            if (-not [string]::IsNullOrWhiteSpace($ExpectedPhase)) { throw 'Untrusted recovery evidence was accepted.' }
        }
        if($WorkerOnly) {
            $ArgumentList=@('apple-release','Ship','--config','powerforge.release.json','--plan')
            try { Register-AppleAutomationEvidence -SourceCommit $Source; throw 'Ordinary Ship accepted a stale plan.' }
            catch { if($_.Exception.Message -notlike '*not bounded to the exact*'){throw} }
            'PASS stale plan retained for worker only'
            exit 0
        }
        $ArgumentList=@('apple-testflight','run','--config','powerforge.release.json')
        $unexpected = Join-Path $consumer 'build/unreviewed.bin'
        [IO.File]::WriteAllText($unexpected, 'unreviewed output')
        try { Assert-ConsumerRepositoryContent; throw 'Unrelated ignored output was accepted.' }
        catch { if ($_.Exception.Message -notlike '*non-reviewed content*') { throw } }
        'PASS clean recovery and unrelated-content rejection'
        """;
}
