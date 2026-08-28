using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Fact]
    public void PinnedAppleReleaseAlwaysForwardsTheVerifiedConsumerCommit()
    {
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-source-forwarding-{Guid.NewGuid():N}");
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        try
        {
            Directory.CreateDirectory(parent);
            var harness = Path.Combine(parent, "source-forwarding-harness.ps1");
            File.WriteAllText(harness,
                $$"""
                param([string] $Support)
                $ErrorActionPreference = 'Stop'
                function Get-OptionValue {
                    param([string] $Option)
                    for ($index = 0; $index -lt $ArgumentList.Count; $index++) {
                        if ($ArgumentList[$index] -eq $Option) { return $ArgumentList[$index + 1] }
                        if ($ArgumentList[$index].StartsWith("$Option=", [StringComparison]::OrdinalIgnoreCase)) {
                            return $ArgumentList[$index].Substring($Option.Length + 1)
                        }
                    }
                    return $null
                }
                . $Support

                $ArgumentList = @('apple-release', 'Status', '--config', 'powerforge.release.json', '--capture-provenance', 'capture.json', '--allowed-root', 'capture')
                $forwarded = @(Get-ForwardedArgumentList -SourceCommit '{{commit}}')
                if (($forwarded -join '|') -ne 'apple-release|Status|--config|powerforge.release.json|--apple-source-commit|{{commit}}') {
                    throw "Verified source commit was not injected: $($forwarded -join '|')"
                }

                $ArgumentList = @('apple-release', 'Status', '--config', 'powerforge.release.json', '--apple-source-commit={{commit}}')
                $forwarded = @(Get-ForwardedArgumentList -SourceCommit '{{commit}}')
                if (($forwarded -join '|') -ne 'apple-release|Status|--config|powerforge.release.json|--apple-source-commit|{{commit}}') {
                    throw "Explicit source commit was not normalized: $($forwarded -join '|')"
                }

                $ArgumentList = @('apple-release', 'Status', '--config', 'powerforge.release.json', '--allowed-root=capture')
                $forwarded = @(Get-ForwardedArgumentList -SourceCommit '{{commit}}')
                if (($forwarded -join '|') -ne 'apple-release|Status|--config|powerforge.release.json|--apple-source-commit|{{commit}}') {
                    throw "The local retained root was forwarded to the release engine: $($forwarded -join '|')"
                }

                foreach ($invalidArguments in @(
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--apple-source-commit', ''),
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--allowed-root'),
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--allowed-root='),
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--allowed-root=-capture'),
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--capture-provenance=-capture.json'),
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--allowed-root', '--plan'),
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--allowed-root', 'capture', '--allowed-root=other'),
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--capture-provenance', 'capture.json', '--capture-provenance=other.json'),
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--apple-source-commit', '{{commit}}', '--apple-source-commit={{commit}}'),
                    @('apple-release', 'Status', '--config', 'powerforge.release.json', '--apple-source-commit', '89abcdef0123456789abcdef0123456789abcdef'))) {
                    $ArgumentList = $invalidArguments
                    try {
                        Get-ForwardedArgumentList -SourceCommit '{{commit}}' | Out-Null
                        throw "Invalid source arguments were accepted: $($ArgumentList -join '|')"
                    } catch {
                        if ($_.Exception.Message -like 'Invalid source arguments were accepted:*') { throw }
                    }
                }

                $ArgumentList = @('apple-governance', 'validate', '--config', 'governance.json')
                $forwarded = @(Get-ForwardedArgumentList -SourceCommit '{{commit}}')
                if (($forwarded -join '|') -ne ($ArgumentList -join '|')) {
                    throw "A non-release command was changed: $($forwarded -join '|')"
                }
                'PASS'
                """);

            var result = Run(
                "pwsh",
                parent,
                "-NoLogo", "-NoProfile", "-File", harness,
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
    public void PinnedAppleReleaseResolvesExactTargetsWithAValidationOnlySummary()
    {
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-target-resolution-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(parent);
            var harness = Path.Combine(parent, "target-resolution-harness.ps1");
            File.WriteAllText(harness,
                """
                param([string] $Support, [string] $Output)
                $ErrorActionPreference = 'Stop'
                . $Support

                $arguments = @(
                    'apple-release', 'Screenshots', '--config', 'powerforge.release.json',
                    '--target', 'App', '--plan', '--confirm-apple-action',
                    '--apple-expected-plan-sha256', ('a' * 64), '--output=text')
                $resolvedArguments = @(Get-AppleTargetResolutionArgumentList -Arguments $arguments)
                $expected = @(
                    'apple-release', 'Screenshots', '--config', 'powerforge.release.json',
                    '--target', 'App', '--validate', '--summary', '--output', 'json')
                if (($resolvedArguments -join '|') -ne ($expected -join '|')) {
                    throw "Unexpected target-resolution arguments: $($resolvedArguments -join '|')"
                }

                [pscustomobject]@{
                    command = 'apple-release'
                    success = $true
                    result = [pscustomobject]@{
                        validateOnly = $true
                        planOnly = $false
                        targets = @(
                            [pscustomobject]@{
                                name = 'App'
                                platform = 'iOS'
                                distributionRoute = 'AppStore'
                                appId = '123'
                                marketingVersion = '1.2.3'
                            })
                    }
                } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Output
                $targets = @(Read-ResolvedAppleTargets -Path $Output)
                if ($targets.Count -ne 1 -or $targets[0].appId -ne '123') {
                    throw 'The exact resolved target identity was not returned.'
                }

                [pscustomobject]@{
                    command = 'apple-release'
                    success = $true
                    result = [pscustomobject]@{
                        validateOnly = $true
                        planOnly = $false
                        targets = @(
                            [pscustomobject]@{
                                name = 'App'
                                platform = 'iOS'
                                distributionRoute = 'AppStore'
                                appId = '123'
                            })
                    }
                } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Output
                try {
                    Read-ResolvedAppleTargets -Path $Output | Out-Null
                    throw 'An App Store target without a marketing version was accepted.'
                } catch {
                    if ($_.Exception.Message -like 'An App Store target without a marketing version was accepted.*') { throw }
                }

                [pscustomobject]@{
                    command = 'apple-release'
                    success = $true
                    result = [pscustomobject]@{
                        validateOnly = $false
                        planOnly = $true
                        targets = @()
                    }
                } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Output
                try {
                    Read-ResolvedAppleTargets -Path $Output | Out-Null
                    throw 'A non-validation target summary was accepted.'
                } catch {
                    if ($_.Exception.Message -like 'A non-validation target summary was accepted.*') { throw }
                }
                'PASS'
                """);

            var result = Run(
                "pwsh",
                parent,
                "-NoLogo", "-NoProfile", "-File", harness,
                "-Support", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.Evidence.ps1"),
                "-Output", Path.Combine(parent, "summary.json"));
            result.EnsureSuccess();
            Assert.Contains("PASS", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

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
    public void ScreenshotEvidenceAllowsAnApprovedSubsetWithinOneExactInventoryRootAndRejectsEveryOtherIgnoredFile()
    {
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-evidence-{Guid.NewGuid():N}");
        var sandbox = Path.Combine(parent, "consumer");
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, "capture", "phone"));
            var nested = Path.Combine(sandbox, "capture", "phone", "home.png");
            File.WriteAllText(nested, "nested screenshot bytes");
            var nestedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nested))).ToLowerInvariant();
            const string commit = "0123456789abcdef0123456789abcdef01234567";
            File.WriteAllText(Path.Combine(sandbox, "powerforge.release.json"),
                """{ "AppleApps": { "ProjectRoot": ".", "SyncScreenshots": true, "ScreenshotConfigPaths": [ "screenshots.json", "other-screenshots.json", "beta-screenshots.json", "historical-screenshots.json" ], "Apps": [ { "Name": " App ", "BundleId": "com.example.app", "Platform": "IOS" }, { "Name": "Other", "BundleId": "com.example.other", "Platform": "IOS" }, { "Name": "Beta", "BundleId": "com.example.beta", "Platform": "IOS", "DistributionRoute": "TestFlightOnly", "AppStoreConnectAppId": "999" }, { "Name": "Direct", "BundleId": "com.example.direct", "Platform": "macOS", "DistributionRoute": "DirectNotarized", "AppStoreConnectAppId": "777" } ] } }""");
            File.WriteAllText(Path.Combine(sandbox, "screenshots.json"),
                """{ "AppId": "123", "Platform": "IOS", "UseReleaseVersion": true, "Quality": { "ApprovalManifestPath": "screenshots.approval.json" } }""");
            File.WriteAllText(Path.Combine(sandbox, "other-screenshots.json"),
                """{ "AppId": "888", "Platform": "IOS", "UseReleaseVersion": true, "Quality": { "ApprovalManifestPath": "missing-other.approval.json" } }""");
            File.WriteAllText(Path.Combine(sandbox, "beta-screenshots.json"),
                """{ "AppId": "999", "Platform": "IOS", "UseReleaseVersion": true, "Quality": { "ApprovalManifestPath": "missing-beta.approval.json" } }""");
            File.WriteAllText(Path.Combine(sandbox, "historical-screenshots.json"),
                """{ "AppId": "123", "Platform": "IOS", "VersionString": "1.1.0", "Quality": { "ApprovalManifestPath": "missing-historical.approval.json" } }""");
            File.WriteAllText(Path.Combine(sandbox, "screenshots.approval.json"), JsonSerializer.Serialize(new
            {
                CaptureRunId = "42",
                CaptureRepository = "EvotecIT/TestApp",
                CaptureWorkflowRef = "EvotecIT/TestApp/.github/workflows/capture.yml@refs/heads/main",
                SourceCommit = commit,
                VersionString = "1.2.3",
                Screenshots = new object[]
                {
                    new { File = "capture/phone/home.png", Sha256 = nestedHash, Width = 100, Height = 200 }
                }
            }));
            File.WriteAllText(Path.Combine(sandbox, ".gitignore"), "capture/\n*.approval.json\n");
            Run("git", sandbox, "init", "--quiet").EnsureSuccess();
            Run("git", sandbox, "add", "powerforge.release.json", "screenshots.json", "other-screenshots.json", "beta-screenshots.json", "historical-screenshots.json", ".gitignore").EnsureSuccess();
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
                        [pscustomobject]@{ path='home.png'; sha256='NESTED_HASH'; width=100; height=200 },
                        [pscustomobject]@{ path='website-candidate/home.png'; sha256='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'; width=1200; height=800 })
                }
                $consumer = [IO.Path]::GetFullPath($Consumer)
                $ArgumentList = @('apple-release','Screenshots','--config','powerforge.release.json','--target','App','--allowed-root','capture')
                function Invoke-GitText { param([string]$Root,[string[]]$Arguments); $o=@(& $script:gitPath -c core.quotePath=false -C $Root @Arguments 2>&1); if($LASTEXITCODE -ne 0){throw 'git failed'}; return ($o -join [Environment]::NewLine).Trim() }
                function Get-OptionValue { param([string]$Option); $i=[Array]::IndexOf($ArgumentList,$Option); if($i -ge 0 -and $i+1 -lt $ArgumentList.Count){return $ArgumentList[$i+1]}; return $null }
                function Resolve-OptionPath { param([string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $consumer $Value)) }
                function Resolve-PathFromBase { param([string]$BasePath,[string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $BasePath $Value)) }
                function Assert-UnlinkedPath { param([string]$Path,[string]$Name,[switch]$AllowMissingLeaf) }
                function Assert-UnlinkedDirectory { param([string]$Path,[string]$Name) }
                . $Support
                $appTargets = @([pscustomobject]@{ name='App'; platform='iOS'; distributionRoute='AppStore'; appId='123'; marketingVersion='1.2.3' })
                Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $appTargets
                $mismatchedVersionTargets = @([pscustomobject]@{ name='App'; platform='iOS'; distributionRoute='AppStore'; appId='123'; marketingVersion='1.2.4' })
                try { Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $mismatchedVersionTargets; throw 'A mismatched target marketing version was accepted.' }
                catch { if ($_.Exception.Message -notlike '*does not match selected Apple app*version*') { throw } }
                $originalArguments = @($ArgumentList)
                $releasePath = Join-Path $consumer 'powerforge.release.json'
                $originalRelease = Get-Content -LiteralPath $releasePath -Raw
                $releaseWithoutScreenshots = $originalRelease | ConvertFrom-Json
                $releaseWithoutScreenshots.AppleApps.ScreenshotConfigPaths = @('', '   ')
                $releaseWithoutScreenshots | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $releasePath
                $originalProvenance = $script:validatedCaptureProvenance
                $script:validatedCaptureProvenance = $null
                $ArgumentList = @('apple-release','Advance','--config','powerforge.release.json','--target','App')
                Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $appTargets
                Set-Content -LiteralPath $releasePath -Value $originalRelease -NoNewline
                foreach ($route in @(
                    [pscustomobject]@{ Target='Beta'; Platform='iOS'; DistributionRoute='TestFlightOnly'; AppId='999' },
                    [pscustomobject]@{ Target='Direct'; Platform='macOS'; DistributionRoute='DirectNotarized'; AppId='777' })) {
                    $ArgumentList = @('apple-release','Advance','--config','powerforge.release.json','--target',$route.Target)
                    $routeTargets = @([pscustomobject]@{ name=$route.Target; platform=$route.Platform; distributionRoute=$route.DistributionRoute; appId=$route.AppId })
                    Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $routeTargets
                }
                $script:validatedCaptureProvenance = $originalProvenance
                $ArgumentList = @('apple-release','Screenshots','--config','powerforge.release.json','--target','App')
                try { Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $appTargets; throw 'Missing retained root was accepted.' }
                catch { if ($_.Exception.Message -notlike '*requires --allowed-root*') { throw } }
                $ArgumentList = $originalArguments
                $approvalPath = Join-Path $consumer 'screenshots.approval.json'
                $originalApproval = Get-Content -LiteralPath $approvalPath -Raw
                $caseVariantApproval = $originalApproval | ConvertFrom-Json
                $caseVariantApproval.Screenshots[0].File = 'capture/phone/Home.png'
                $caseVariantPath = Join-Path $consumer 'capture/phone/Home.png'
                $caseVariantCreated = $false
                if (-not (Test-Path -LiteralPath $caseVariantPath -PathType Leaf)) {
                    Copy-Item -LiteralPath (Join-Path $consumer 'capture/phone/home.png') -Destination $caseVariantPath
                    $caseVariantCreated = $true
                }
                $caseVariantApproval | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $approvalPath
                if ($IsWindows) {
                    Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $appTargets
                } else {
                    try { Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $appTargets; throw 'Case-variant inventory path was accepted.' }
                    catch { if ($_.Exception.Message -notlike '*approved inventory*') { throw } }
                }
                Set-Content -LiteralPath $approvalPath -Value $originalApproval -NoNewline
                if ($caseVariantCreated) { Remove-Item -LiteralPath $caseVariantPath }
                $originalProvenanceScreenshots = @($script:validatedCaptureProvenance.screenshots)
                $script:validatedCaptureProvenance.screenshots += [pscustomobject]@{
                    path = 'home.png';
                    sha256 = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';
                    width = 101;
                    height = 200
                }
                try { Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $appTargets; throw 'Conflicting provenance path was accepted.' }
                catch { if ($_.Exception.Message -notlike '*duplicate screenshot path*') { throw } }
                $script:validatedCaptureProvenance.screenshots = $originalProvenanceScreenshots
                $duplicateApproval = $originalApproval | ConvertFrom-Json
                $duplicateApproval.Screenshots += $duplicateApproval.Screenshots[0]
                $duplicateApproval | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $approvalPath
                try { Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $appTargets; throw 'Duplicate approved screenshot was accepted.' }
                catch { if ($_.Exception.Message -notlike '*duplicate approved screenshot path*') { throw } }
                Set-Content -LiteralPath $approvalPath -Value $originalApproval -NoNewline
                $unretainedPath = Join-Path $consumer 'capture/unretained.png'
                Set-Content -LiteralPath $unretainedPath -Value 'unretained screenshot bytes'
                $approval = $originalApproval | ConvertFrom-Json
                $approval.Screenshots += [pscustomobject]@{
                    File = 'capture/unretained.png';
                    Sha256 = (Get-FileHash -LiteralPath $unretainedPath -Algorithm SHA256).Hash;
                    Width = 100;
                    Height = 200
                }
                $approval | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $approvalPath
                try { Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $appTargets; throw 'Unretained screenshot was accepted.' }
                catch { if ($_.Exception.Message -notlike '*approved inventory*') { throw } }
                Set-Content -LiteralPath $approvalPath -Value $originalApproval -NoNewline
                Remove-Item -LiteralPath $unretainedPath
                Assert-ConsumerRepositoryContent
                Set-Content -LiteralPath (Join-Path $consumer 'capture/injected.bin') -Value 'not reviewed'
                try { Assert-ConsumerRepositoryContent; throw 'Unreviewed file was accepted.' }
                catch { if ($_.Exception.Message -notlike '*non-reviewed content*') { throw }; 'PASS' }
                """
                .Replace("NESTED_HASH", nestedHash, StringComparison.Ordinal));

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
    public void RemoteScreenshotSubmissionDoesNotRequireLocalCaptureProvenance()
    {
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-remote-screenshots-{Guid.NewGuid():N}");
        var sandbox = Path.Combine(parent, "consumer");
        try
        {
            Directory.CreateDirectory(sandbox);
            File.WriteAllText(Path.Combine(sandbox, "powerforge.release.json"),
                """{ "AppleApps": { "ProjectRoot": ".", "ScreenshotConfigPaths": [ "screenshots.json" ], "Apps": [ { "Name": "App", "Platform": "IOS", "AppStoreConnectAppId": "123" } ] } }""");
            File.WriteAllText(Path.Combine(sandbox, "screenshots.json"),
                """{ "AppId": "123", "Platform": "IOS", "Quality": { "ApprovalManifestPath": "missing.approval.json" } }""");

            var harness = Path.Combine(parent, "remote-screenshot-harness.ps1");
            File.WriteAllText(harness,
                """
                param([string] $Consumer, [string] $Support)
                $ErrorActionPreference = 'Stop'
                $consumer = [IO.Path]::GetFullPath($Consumer)
                $script:allowedConsumerEvidencePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                function Get-OptionValue { param([string]$Option); $i=[Array]::IndexOf($ArgumentList,$Option); if($i -ge 0 -and $i+1 -lt $ArgumentList.Count){return $ArgumentList[$i+1]}; return $null }
                function Resolve-OptionPath { param([string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $consumer $Value)) }
                function Resolve-PathFromBase { param([string]$BasePath,[string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $BasePath $Value)) }
                . $Support
                $ArgumentList = @('apple-release','SubmitAppReview','--config','powerforge.release.json')
                Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567'
                'SUBMIT_PASS'
                $ArgumentList = @('apple-release','Screenshots','--config','powerforge.release.json')
                $appTargets = @([pscustomobject]@{ name='App'; platform='iOS'; distributionRoute='AppStore'; appId='123'; marketingVersion='1.2.3' })
                try { Assert-ScreenshotPublicationBinding -SourceCommit '0123456789abcdef0123456789abcdef01234567' -ResolvedTargets $appTargets; throw 'Screenshots accepted missing capture provenance.' }
                catch { if ($_.Exception.Message -notlike '*requires --capture-provenance*') { throw }; 'SCREENSHOTS_BLOCKED' }
                """);

            var result = Run(
                "pwsh",
                parent,
                "-NoLogo", "-NoProfile", "-File", harness,
                "-Consumer", sandbox,
                "-Support", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.Evidence.ps1"));
            result.EnsureSuccess();
            Assert.Contains("SUBMIT_PASS", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("SCREENSHOTS_BLOCKED", result.StandardOutput, StringComparison.Ordinal);
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
            Directory.CreateDirectory(parent);
            var authenticationKeyPath = Path.Combine(parent, "apple-receipt-auth.key");
            var authenticationKey = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
            File.WriteAllBytes(authenticationKeyPath, authenticationKey);
            string Authenticate(string receiptSha256)
            {
                using var hmac = new HMACSHA256(authenticationKey);
                return Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes(receiptSha256))).ToLowerInvariant();
            }
            var output = Path.Combine(sandbox, "build", "powerforge", "apple");
            Directory.CreateDirectory(output);
            var receiptHistory = Directory.CreateDirectory(Path.Combine(output, "receipts"));
            File.WriteAllText(Path.Combine(sandbox, "powerforge.release.json"),
                """{ "AppleApps": { "ProjectRoot": ".", "Automation": { "ReceiptPath": "build/powerforge/apple/release-receipt.json", "ReceiptHistoryPath": "build/powerforge/apple/receipts", "PlanReceiptPath": "build/powerforge/apple/release-plan.json", "LockPath": "build/powerforge/apple/release.lock" }, "Apps": [ { "Enabled": true, "DistributionRoute": "AppStore", "ProjectPath": "Sample.xcodeproj" } ] } }""");
            File.WriteAllText(Path.Combine(sandbox, ".gitignore"), "build/\n");
            const string latestSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            File.WriteAllText(Path.Combine(output, "release-receipt.json"), JsonSerializer.Serialize(new {
                schemaVersion = 6, attemptId = "00000000000000000000000000000000", receiptSha256 = latestSha,
                sourceCommit = commit }));
            File.WriteAllText(Path.Combine(receiptHistory.FullName, "prior-upload.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 5,
                attemptId = "11111111111111111111111111111111",
                receiptSha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                receiptAuthenticationSha256 = Authenticate("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
                sourceCommit = "89abcdef0123456789abcdef0123456789abcdef"
            }));
            File.WriteAllText(Path.Combine(receiptHistory.FullName, "local-status.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 5,
                attemptId = "22222222222222222222222222222222",
                receiptSha256 = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                receiptAuthenticationSha256 = Authenticate("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"),
                action = "Status"
            }));
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
                $env:POWERFORGE_APPLE_RECEIPT_AUTH_KEY_PATH = '{{authenticationKeyPath.Replace("'", "''", StringComparison.Ordinal)}}'
                $ArgumentList = @('apple-release','Advance','--config','powerforge.release.json','--apple-expected-plan-sha256','{{planSha256}}')
                function Invoke-GitText { param([string]$Root,[string[]]$Arguments); $o=@(& $script:gitPath -c core.quotePath=false -C $Root @Arguments 2>&1); if($LASTEXITCODE -ne 0){throw 'git failed'}; return ($o -join [Environment]::NewLine).Trim() }
                function Get-OptionValue { param([string]$Option); $i=[Array]::IndexOf($ArgumentList,$Option); if($i -ge 0 -and $i+1 -lt $ArgumentList.Count){return $ArgumentList[$i+1]}; return $null }
                function Resolve-OptionPath { param([string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $consumer $Value)) }
                function Resolve-PathFromBase { param([string]$BasePath,[string]$Value); if([IO.Path]::IsPathRooted($Value)){return [IO.Path]::GetFullPath($Value)}; return [IO.Path]::GetFullPath((Join-Path $BasePath $Value)) }
                function Assert-UnlinkedPath { param([string]$Path,[string]$Name,[switch]$AllowMissingLeaf) }
                function Assert-UnlinkedDirectory { param([string]$Path,[string]$Name) }
                . $Support
                Register-AppleAutomationEvidence -SourceCommit '{{commit}}'
                Assert-ConsumerRepositoryContent
                foreach ($nextArguments in @(
                    @('apple-release','Status','--config','powerforge.release.json'),
                    @('apple-release','SubmitAppReview','--config','powerforge.release.json','--plan'))) {
                    $script:allowedConsumerEvidencePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                    $ArgumentList = $nextArguments
                    Register-AppleAutomationEvidence -SourceCommit '{{commit}}'
                    Assert-ConsumerRepositoryContent
                }
                Set-Content -LiteralPath (Join-Path $consumer 'build/powerforge/apple/receipts/injected.bin') -Value 'not a receipt'
                try { Register-AppleAutomationEvidence -SourceCommit '{{commit}}'; throw 'Unsupported receipt history was accepted.' }
                catch { if ($_.Exception.Message -notlike '*unsupported entry*') { throw } }
                Remove-Item -LiteralPath (Join-Path $consumer 'build/powerforge/apple/receipts/injected.bin')
                $latestReceipt = Join-Path $consumer 'build/powerforge/apple/release-receipt.json'
                $savedLatestReceipt = Get-Content -LiteralPath $latestReceipt -Raw
                Set-Content -LiteralPath $latestReceipt -Value '{"schemaVersion":3,"sourceCommit":"89abcdef0123456789abcdef0123456789abcdef"}'
                Register-AppleAutomationEvidence -SourceCommit '{{commit}}'
                Set-Content -LiteralPath $latestReceipt -Value $savedLatestReceipt -NoNewline
                $historyDirectory = Join-Path $consumer 'build/powerforge/apple/receipts'
                $historyBackup = Join-Path (Split-Path -Parent $consumer) 'receipts-backup'
                Move-Item -LiteralPath $historyDirectory -Destination $historyBackup
                Set-Content -LiteralPath $latestReceipt -Value '{"schemaVersion":4,"attemptId":"ffffffffffffffffffffffffffffffff","receiptSha256":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd","sourceCommit":"{{commit}}"}'
                try { Register-AppleAutomationEvidence -SourceCommit '{{commit}}'; throw 'Forged self-hashed receipt was accepted.' }
                catch { if ($_.Exception.Message -notlike '*without a supported current receipt chain*') { throw } }
                Move-Item -LiteralPath $historyBackup -Destination $historyDirectory
                Set-Content -LiteralPath $latestReceipt -Value $savedLatestReceipt -NoNewline
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
