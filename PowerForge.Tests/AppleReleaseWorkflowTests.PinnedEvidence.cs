using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Fact]
    public void TrackedSourceLinksMustRemainInsideTheExactConsumerCheckout()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-source-links-{Guid.NewGuid():N}");
        var sandbox = Path.Combine(parent, "consumer");
        try
        {
            Directory.CreateDirectory(sandbox);
            File.WriteAllText(Path.Combine(sandbox, "target.txt"), "tracked target");
            File.CreateSymbolicLink(Path.Combine(sandbox, "internal-link.txt"), "target.txt");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", ".").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Contained source link");

            var harness = Path.Combine(parent, "link-harness.ps1");
            File.WriteAllText(harness,
                """
                param([string] $Consumer, [string] $Support)
                $ErrorActionPreference = 'Stop'
                $script:gitPath = '/usr/bin/git'
                $consumer = [IO.Path]::GetFullPath($Consumer)
                function Invoke-GitText { param([string]$Root,[string[]]$Arguments); $o=@(& $script:gitPath -c core.quotePath=false -C $Root @Arguments 2>&1); if($LASTEXITCODE -ne 0){throw 'git failed'}; return ($o -join [Environment]::NewLine).Trim() }
                . $Support
                Assert-TrackedSourceLinks
                'PASS'
                """);
            var accepted = Run("pwsh", parent, "-NoLogo", "-NoProfile", "-File", harness,
                "-Consumer", sandbox, "-Support", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.Evidence.ps1"));
            accepted.EnsureSuccess();

            var linkedCommit = Run("git", sandbox, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();
            Run("git", sandbox, "update-index", "--add", "--cacheinfo", $"160000,{linkedCommit},nested-module").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Tracked submodule entry");
            var submoduleRejected = Run("pwsh", parent, "-NoLogo", "-NoProfile", "-File", harness,
                "-Consumer", sandbox, "-Support", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.Evidence.ps1"));
            Assert.NotEqual(0, submoduleRejected.ExitCode);
            Assert.Contains("Tracked Git submodules are forbidden", submoduleRejected.StandardOutput + submoduleRejected.StandardError, StringComparison.Ordinal);
            Run("git", sandbox, "rm", "--cached", "nested-module").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Remove submodule entry");

            File.WriteAllText(Path.Combine(parent, "outside.txt"), "unreviewed external bytes");
            File.CreateSymbolicLink(Path.Combine(sandbox, "escaping-link.txt"), "../outside.txt");
            Run("git", sandbox, "add", "escaping-link.txt").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Escaping source link");
            var rejected = Run("pwsh", parent, "-NoLogo", "-NoProfile", "-File", harness,
                "-Consumer", sandbox, "-Support", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.Evidence.ps1"));

            Assert.NotEqual(0, rejected.ExitCode);
            Assert.Contains("escapes the exact consumer checkout", rejected.StandardOutput + rejected.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void CleanupTrackedInputValidationAllowsAProjectRemovedByTheRelease()
    {
        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"powerforge-cleanup-project-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, ".powerforge"));
            var configPath = Path.Combine(sandbox, "powerforge.release.json");
            var manifestPath = Path.Combine(sandbox, ".powerforge", "powerforge.tool.json");
            File.WriteAllText(configPath,
                """{ "AppleApps": { "ProjectRoot": ".", "Apps": [ { "ProjectPath": "Removed.xcodeproj" } ] } }""");
            File.WriteAllText(manifestPath, "{}");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", "powerforge.release.json", ".powerforge/powerforge.tool.json").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Cleanup source");
            var commit = Run("git", sandbox, "rev-parse", "HEAD").StandardOutput.Trim();

            var normal = RunTrackedReleaseInputValidator(root, sandbox, configPath, manifestPath, commit);
            Assert.NotEqual(0, normal.ExitCode);
            Assert.Contains("ProjectPath was not found", normal.StandardError + normal.StandardOutput, StringComparison.OrdinalIgnoreCase);

            var cleanup = RunTrackedReleaseInputValidator(
                root,
                sandbox,
                configPath,
                manifestPath,
                commit,
                allowMissingProject: true);
            cleanup.EnsureSuccess();
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void ScreenshotEvidenceUsesOneExactInventoryRootAndRejectsEveryOtherIgnoredFile()
    {
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-evidence-{Guid.NewGuid():N}");
        var sandbox = Path.Combine(parent, "consumer");
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, "capture", "phone"));
            var nested = Path.Combine(sandbox, "capture", "phone", "home.png");
            var rootImage = Path.Combine(sandbox, "capture", "home.png");
            File.WriteAllText(nested, "nested screenshot bytes");
            File.WriteAllText(rootImage, "root screenshot bytes");
            var nestedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nested))).ToLowerInvariant();
            var rootHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rootImage))).ToLowerInvariant();
            const string commit = "0123456789abcdef0123456789abcdef01234567";
            File.WriteAllText(Path.Combine(sandbox, "powerforge.release.json"),
                """{ "AppleApps": { "ProjectRoot": ".", "ScreenshotConfigPaths": [ "screenshots.json" ], "Apps": [ { "Name": "App", "Platform": "IOS", "AppStoreConnectAppId": "123" } ] } }""");
            File.WriteAllText(Path.Combine(sandbox, "screenshots.json"),
                """{ "AppId": "123", "Platform": "IOS", "Quality": { "ApprovalManifestPath": "screenshots.approval.json" } }""");
            File.WriteAllText(Path.Combine(sandbox, "screenshots.approval.json"), JsonSerializer.Serialize(new
            {
                CaptureRunId = "42",
                CaptureRepository = "EvotecIT/TestApp",
                CaptureWorkflowRef = "EvotecIT/TestApp/.github/workflows/capture.yml@refs/heads/main",
                SourceCommit = commit,
                VersionString = "1.2.3",
                Screenshots = new object[]
                {
                    new { File = "capture/phone/home.png", Sha256 = nestedHash, Width = 100, Height = 200 },
                    new { File = "capture/home.png", Sha256 = rootHash, Width = 100, Height = 200 }
                }
            }));
            File.WriteAllText(Path.Combine(sandbox, ".gitignore"), "capture/\n*.approval.json\n");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", "powerforge.release.json", "screenshots.json", ".gitignore").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Tracked screenshot configuration");

            var harness = Path.Combine(parent, "evidence-harness.ps1");
            File.WriteAllText(harness,
                """
                param([string] $Consumer, [string] $Support)
                $ErrorActionPreference = 'Stop'
                $script:gitPath = '/usr/bin/git'
                $script:allowedConsumerEvidencePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $script:validatedCaptureProvenance = [pscustomobject]@{
                    captureRunId = '42'; repository = 'EvotecIT/TestApp';
                    workflowRef = 'EvotecIT/TestApp/.github/workflows/capture.yml@refs/heads/main';
                    marketingVersion = '1.2.3'; screenshots = @(
                        [pscustomobject]@{ path='phone/home.png'; sha256='NESTED_HASH'; width=100; height=200 },
                        [pscustomobject]@{ path='home.png'; sha256='ROOT_HASH'; width=100; height=200 })
                }
                $consumer = [IO.Path]::GetFullPath($Consumer)
                $ArgumentList = @('apple-release','Screenshots','--config','powerforge.release.json')
                function Invoke-GitText { param([string]$Root,[string[]]$Arguments); $o=@(& $script:gitPath -c core.quotePath=false -C $Root @Arguments 2>&1); if($LASTEXITCODE -ne 0){throw 'git failed'}; return ($o -join [Environment]::NewLine).Trim() }
                function Get-OptionValue { param([string]$Option); $i=[Array]::IndexOf($ArgumentList,$Option); if($i -ge 0 -and $i+1 -lt $ArgumentList.Count){return $ArgumentList[$i+1]}; return $null }
                function Resolve-OptionPath { param([string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $consumer $Value)) }
                function Resolve-PathFromBase { param([string]$BasePath,[string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $BasePath $Value)) }
                function Assert-UnlinkedPath { param([string]$Path,[string]$Name,[switch]$AllowMissingLeaf) }
                . $Support
                Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567'
                Assert-ConsumerRepositoryContent
                Set-Content -LiteralPath (Join-Path $consumer 'capture/injected.bin') -Value 'not reviewed'
                try { Assert-ConsumerRepositoryContent; throw 'Unreviewed file was accepted.' }
                catch { if ($_.Exception.Message -notlike '*non-reviewed content*') { throw }; 'PASS' }
                """
                .Replace("NESTED_HASH", nestedHash, StringComparison.Ordinal)
                .Replace("ROOT_HASH", rootHash, StringComparison.Ordinal));

            var result = Run(
                "pwsh",
                parent,
                "-NoLogo", "-NoProfile", "-File", harness,
                "-Consumer", sandbox,
                "-Support", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.Evidence.ps1"));
            result.EnsureSuccess();
            Assert.Contains("PASS", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ApprovedApplePlanAndBoundAutomationOutputsDoNotRequireASecondCheckout()
    {
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-plan-evidence-{Guid.NewGuid():N}");
        var sandbox = Path.Combine(parent, "consumer");
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        const string planSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        try
        {
            var output = Path.Combine(sandbox, "build", "powerforge", "apple");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(sandbox, "powerforge.release.json"),
                """{ "AppleApps": { "ProjectRoot": ".", "Automation": { "ReceiptPath": "build/powerforge/apple/release-receipt.json", "PlanReceiptPath": "build/powerforge/apple/release-plan.json", "LockPath": "build/powerforge/apple/release.lock" }, "Apps": [ { "Enabled": true, "DistributionRoute": "AppStore", "ProjectPath": "Sample.xcodeproj" } ] } }""");
            File.WriteAllText(Path.Combine(sandbox, ".gitignore"), "build/\n");
            File.WriteAllText(Path.Combine(output, "release-receipt.json"), JsonSerializer.Serialize(new { sourceCommit = commit }));
            File.WriteAllText(Path.Combine(output, "release-plan.json"), JsonSerializer.Serialize(new
            {
                planOnly = true,
                action = "Advance",
                sourceCommit = commit,
                planSha256
            }));
            File.WriteAllText(Path.Combine(output, "release.lock"), "stale generated lock state");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", "powerforge.release.json", ".gitignore").EnsureSuccess();
            CommitTrackedReleaseSandbox(sandbox, "Tracked release configuration");

            var harness = Path.Combine(parent, "plan-evidence-harness.ps1");
            File.WriteAllText(harness,
                $$"""
                param([string] $Consumer, [string] $Support)
                $ErrorActionPreference = 'Stop'
                $script:gitPath = '/usr/bin/git'
                $script:allowedConsumerEvidencePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $consumer = [IO.Path]::GetFullPath($Consumer)
                $ArgumentList = @('apple-release','Advance','--config','powerforge.release.json','--apple-expected-plan-sha256','{{planSha256}}')
                function Invoke-GitText { param([string]$Root,[string[]]$Arguments); $o=@(& $script:gitPath -c core.quotePath=false -C $Root @Arguments 2>&1); if($LASTEXITCODE -ne 0){throw 'git failed'}; return ($o -join [Environment]::NewLine).Trim() }
                function Get-OptionValue { param([string]$Option); $i=[Array]::IndexOf($ArgumentList,$Option); if($i -ge 0 -and $i+1 -lt $ArgumentList.Count){return $ArgumentList[$i+1]}; return $null }
                function Resolve-OptionPath { param([string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $consumer $Value)) }
                function Resolve-PathFromBase { param([string]$BasePath,[string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $BasePath $Value)) }
                function Assert-UnlinkedPath { param([string]$Path,[string]$Name,[switch]$AllowMissingLeaf) }
                . $Support
                Register-AppleAutomationEvidence -SourceCommit '{{commit}}'
                Assert-ConsumerRepositoryContent
                Set-Content -LiteralPath (Join-Path $consumer 'build/powerforge/apple/injected.bin') -Value 'not reviewed'
                try { Assert-ConsumerRepositoryContent; throw 'Unreviewed file was accepted.' }
                catch { if ($_.Exception.Message -notlike '*non-reviewed content*') { throw }; 'PASS' }
                """);

            var result = Run(
                "pwsh",
                parent,
                "-NoLogo", "-NoProfile", "-File", harness,
                "-Consumer", sandbox,
                "-Support", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.Evidence.ps1"));
            result.EnsureSuccess();
            Assert.Contains("PASS", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }
}
