namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Theory]
    [InlineData("manifest")]
    [InlineData("manifests")]
    [InlineData("Screenshots")]
    public void PinnedScreenshotCommandsRequireAnExactReviewedDefaultBranchAncestor(string operation)
    {
        if (OperatingSystem.IsWindows()) return; // Apple operator uses PowerShell 7 on the signing Mac.
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-captured-source-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(parent);
            var harness = Path.Combine(parent, "captured-source.ps1");
            File.WriteAllText(harness, ScreenshotSourcePolicyHarness);
            var result = Run("pwsh", parent, "-NoLogo", "-NoProfile", "-File", harness,
                "-Helper", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.ps1"),
                "-Fixture", parent, "-Operation", operation);
            result.EnsureSuccess();
            Assert.Contains("CAPTURED_ANCESTOR_PASS", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("WRONG_CAPTURE_BLOCKED", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("UNMERGED_SOURCE_BLOCKED", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("DIRTY_INPUT_BLOCKED", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("SHIP_LATEST_MAIN_UNCHANGED", result.StandardOutput, StringComparison.Ordinal);
        }
        finally { DeletePinnedEvidenceDirectory(parent); }
    }

    private const string ScreenshotSourcePolicyHarness = """
        param([string]$Helper,[string]$Fixture,[string]$Operation)
        $ErrorActionPreference='Stop'
        foreach($name in @('APP_STORE_CONNECT_KEY_ID','APP_STORE_CONNECT_ISSUER_ID','APP_STORE_CONNECT_PRIVATE_KEY_PATH','APP_STORE_CONNECT_PRIVATE_KEY','ASC_KEY_ID','ASC_ISSUER_ID','ASC_PRIVATE_KEY_PATH','ASC_PRIVATE_KEY')) {
            [Environment]::SetEnvironmentVariable($name,$null)
        }
        $tokens=$null; $errors=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile($Helper,[ref]$tokens,[ref]$errors)
        if($errors.Count){throw 'Pinned helper failed to parse.'}
        foreach($function in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Parent -is [Management.Automation.Language.NamedBlockAst]},$true)) {
            Invoke-Expression $function.Extent.Text
        }
        . (Join-Path (Split-Path -Parent $Helper) 'Invoke-PinnedPowerForge.Evidence.ps1')
        $script:gitPath=(Get-Command git -CommandType Application | Select-Object -First 1).Source
        $consumer=Join-Path $Fixture 'consumer'
        $origin=Join-Path $Fixture 'origin.git'
        $temporaryRoot=Join-Path $Fixture 'operator-temp'
        $requiredBranch='main'
        $ExpectedConsumerRepository='Synthetic/ScreenshotFixture'
        New-Item -ItemType Directory -Path $consumer,$temporaryRoot -Force | Out-Null
        # Only GitHub transport identity is substituted by the local bare remote.
        # Source checkout, fresh fetch, branch, ancestry and content checks are real.
        function Assert-GitHubOrigin {
            param([string]$Root,[string]$ExpectedRepository)
            if($ExpectedRepository -ne 'Synthetic/ScreenshotFixture' -or
                (Invoke-GitText -Root $Root -Arguments @('remote','get-url','origin')) -ne $origin) {
                throw 'Fixture remote identity mismatch.'
            }
        }
        function Reject {
            param([scriptblock]$Action,[string]$Fragment)
            try { & $Action | Out-Null; throw 'Contract failure: unsafe input was accepted.' }
            catch {
                if($_.Exception.Message.StartsWith('Contract failure:') -or
                    ($Fragment -and $_.Exception.Message.IndexOf($Fragment,[StringComparison]::OrdinalIgnoreCase) -lt 0)){throw}
            }
        }
        function CommitFixture {
            param([string]$Message)
            Invoke-GitText -Root $consumer -Arguments @('add','.') | Out-Null
            Invoke-GitText -Root $consumer -Arguments @('-c','commit.gpgsign=false','-c','user.name=PowerForge Tests','-c','user.email=tests@example.invalid','commit','--quiet','-m',$Message) | Out-Null
            return Invoke-GitText -Root $consumer -Arguments @('rev-parse','HEAD')
        }
        Invoke-GitText -Root $consumer -Arguments @('init','--quiet') | Out-Null
        Invoke-GitText -Root $consumer -Arguments @('symbolic-ref','HEAD','refs/heads/main') | Out-Null
        [IO.File]::WriteAllText((Join-Path $consumer '.gitignore'),"capture/`n")
        [IO.File]::WriteAllText((Join-Path $consumer 'source.txt'),'captured application source')
        [IO.File]::WriteAllText((Join-Path $consumer 'screenshots.json'),'{"AppId":"123","Platform":"IOS","VersionString":"1.2.3","Quality":{"ApprovalManifestPath":"capture/approval.json"}}')
        [IO.File]::WriteAllText((Join-Path $consumer 'powerforge.release.json'),'{"AppleApps":{"ProjectRoot":".","ScreenshotConfigPath":"screenshots.json","Apps":[{"Name":"App","Platform":"IOS","BundleId":"com.example.screenshot-fixture","AppStoreConnectAppId":"123"}]}}')
        $captured=CommitFixture 'Accepted capture source'
        Invoke-GitText -Root $Fixture -Arguments @('clone','--quiet','--bare','--no-hardlinks',$consumer,$origin) | Out-Null
        Invoke-GitText -Root $consumer -Arguments @('remote','add','origin',$origin) | Out-Null
        [IO.File]::WriteAllText((Join-Path $consumer 'cloudflare.txt'),'unrelated tooling change')
        $latest=CommitFixture 'Unrelated default branch change'
        Invoke-GitText -Root $consumer -Arguments @('push','--quiet','origin','main') | Out-Null
        Invoke-GitText -Root $consumer -Arguments @('checkout','--quiet','-B','main',$captured) | Out-Null
        Invoke-GitText -Root $consumer -Arguments @('update-ref','refs/remotes/origin/main',$captured) | Out-Null

        $capture=Join-Path $consumer 'capture'
        New-Item -ItemType Directory -Path $capture -Force | Out-Null
        $png=Join-Path $capture 'image.png'
        [IO.File]::WriteAllBytes($png,[Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg=='))
        $hash=(Get-FileHash -LiteralPath $png -Algorithm SHA256).Hash.ToLowerInvariant()
        $provenancePath=Join-Path $capture 'powerforge-apple-screenshot-provenance.json'
        $retainedPath=Join-Path $Fixture 'retained-provenance.json'
        $runPath=Join-Path $Fixture 'retained-run.json'
        $workflowRef="$ExpectedConsumerRepository/.github/workflows/capture.yml@refs/heads/main"
        $provenance=[pscustomobject]@{ repository=$ExpectedConsumerRepository;captureRunId='101';sourceCommit=$captured;workflowRef=$workflowRef;marketingVersion='1.2.3';screenshots=@([pscustomobject]@{path='image.png';sha256=$hash;width=1;height=1}) }
        $provenance | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $provenancePath
        Copy-Item -LiteralPath $provenancePath -Destination $retainedPath
        $run=[pscustomobject]@{status='completed';conclusion='success';event='workflow_dispatch';path='.github/workflows/capture.yml';head_repository=[pscustomobject]@{full_name=$ExpectedConsumerRepository};head_branch='main';head_sha=$captured}
        $run | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runPath
        [pscustomobject]@{CaptureRunId='101';CaptureRepository=$ExpectedConsumerRepository;CaptureWorkflowRef=$workflowRef;SourceCommit=$captured;VersionString='1.2.3';Screenshots=@([pscustomobject]@{File='capture/image.png';Sha256=$hash;Width=1;Height=1})} |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $capture 'approval.json')
        $gh=Join-Path $Fixture 'gh-capture-fixture'
        $ghScript=$gh+'.ps1'
        @'
        #!/usr/bin/env -S pwsh -NoProfile -File
        param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
        $ErrorActionPreference='Stop'
        $root=Split-Path -Parent $PSCommandPath
        if($Arguments[0] -eq 'api') { Get-Content -LiteralPath (Join-Path $root 'retained-run.json') -Raw;exit 0 }
        if($Arguments[0] -eq 'run' -and $Arguments[1] -eq 'download') {
            $run=Get-Content -LiteralPath (Join-Path $root 'retained-run.json') -Raw | ConvertFrom-Json
            $name=$Arguments[[Array]::IndexOf($Arguments,'--name')+1]
            if($name -ne "powerforge-apple-screenshot-provenance-$($run.head_sha)"){exit 3}
            $dest=$Arguments[[Array]::IndexOf($Arguments,'--dir')+1]
            New-Item -ItemType Directory -Path $dest -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $root 'retained-provenance.json') -Destination (Join-Path $dest 'powerforge-apple-screenshot-provenance.json')
            exit 0
        }
        exit 4
        '@ | Set-Content -LiteralPath $ghScript -NoNewline
        @'
        #!/bin/sh
        exec pwsh -NoProfile -File "$0.ps1" "$@"
        '@ | Set-Content -LiteralPath $gh -NoNewline
        & chmod +x $gh
        if($LASTEXITCODE){throw 'Could not mark the network fixture executable.'}
        $configOption=if($Operation -eq 'manifests'){'--release-config'}else{'--config'}
        $config=if($Operation -in @('manifests','Screenshots')){'powerforge.release.json'}else{'screenshots.json'}
        $command=if($Operation -eq 'Screenshots'){'apple-release'}else{'apple-screenshots'}
        $requestedArguments=@($command,$Operation,$configOption,$config,'--capture-provenance',$provenancePath,'--allowed-root',$capture)
        $ArgumentList=$requestedArguments
        $script:allowedConsumerEvidencePaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $consumerHead=Assert-ConsumerSourceRepository
        if($consumerHead -ne $captured -or (Invoke-GitText -Root $consumer -Arguments @('rev-parse','origin/main')) -ne $latest){throw 'Captured ancestor was not admitted against freshly fetched main.'}
        Assert-SafeArguments
        Assert-AuthoritativeCaptureProvenance -GhPath $gh -SourceCommit $captured.ToUpperInvariant()
        $targets=@([pscustomobject]@{name='App';platform='iOS';distributionRoute='AppStore';appId='123';marketingVersion='1.2.3'})
        if($Operation -eq 'Screenshots') { Assert-ScreenshotPublicationBinding -SourceCommit $captured -ResolvedTargets $targets }
        else { Register-StandaloneScreenshotEvidence }
        Assert-ConsumerRepositoryContent
        'CAPTURED_ANCESTOR_PASS'

        # A separately successful retained main capture still cannot change the admitted source.
        $provenance.sourceCommit=$latest; $run.head_sha=$latest
        $provenance | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $provenancePath
        Copy-Item -LiteralPath $provenancePath -Destination $retainedPath
        $run | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runPath
        Reject {Assert-AuthoritativeCaptureProvenance -GhPath $gh -SourceCommit $captured} 'exact consumer HEAD'
        'WRONG_CAPTURE_BLOCKED'
        $provenance.sourceCommit=$captured; $run.head_sha=$captured
        $provenance | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $provenancePath
        Copy-Item -LiteralPath $provenancePath -Destination $retainedPath
        $run | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runPath
        Assert-AuthoritativeCaptureProvenance -GhPath $gh -SourceCommit $captured
        $run.conclusion='failure'; $run | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runPath
        Reject {Assert-AuthoritativeCaptureProvenance -GhPath $gh -SourceCommit $captured} 'dedicated successful default-branch'
        $run.conclusion='success'; $run.head_branch='feature'; $run | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runPath
        Reject {Assert-AuthoritativeCaptureProvenance -GhPath $gh -SourceCommit $captured} 'dedicated successful default-branch'
        $run.head_branch='main'; $run | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runPath
        [IO.File]::AppendAllText($provenancePath,' ')
        Reject {Assert-AuthoritativeCaptureProvenance -GhPath $gh -SourceCommit $captured} 'differs from the retained'
        Copy-Item -LiteralPath $retainedPath -Destination $provenancePath
        if($Operation -eq 'Screenshots') {
            $targets[0].marketingVersion='1.2.4'
            Reject {Assert-ScreenshotPublicationBinding -SourceCommit $captured -ResolvedTargets $targets} 'does not match selected Apple app'
            $targets[0].marketingVersion='1.2.3'
        }
        [IO.File]::AppendAllText($png,'changed image bytes')
        if($Operation -eq 'Screenshots'){Reject {Assert-ScreenshotPublicationBinding -SourceCommit $captured -ResolvedTargets $targets} 'bytes do not match'}
        else{Reject {Register-StandaloneScreenshotEvidence} 'does not match retained capture bytes'}

        [IO.File]::AppendAllText((Join-Path $consumer $config),' ')
        Reject {Assert-SafeArguments} 'Git failed'
        [IO.File]::WriteAllText((Join-Path $consumer 'source.txt'),'dirty application input')
        Reject {Assert-ConsumerRepositoryContent} 'non-reviewed content'
        Invoke-GitText -Root $consumer -Arguments @('checkout','--quiet','HEAD','--',$config,'source.txt') | Out-Null
        [IO.File]::WriteAllText((Join-Path $capture 'unreviewed.bin'),'unreviewed ignored input')
        Reject {Assert-ConsumerRepositoryContent} 'non-reviewed content'
        Remove-Item -LiteralPath (Join-Path $capture 'unreviewed.bin')
        'DIRTY_INPUT_BLOCKED'
        $ArgumentList=$requestedArguments+@('--capture-provenance='+$provenancePath)
        Reject {Assert-ConsumerSourceRepository} 'Duplicate option'
        $ArgumentList=$requestedArguments+@($configOption.ToUpperInvariant()+'='+$config)
        Reject {Assert-SafeArguments} 'Duplicate option'
        if($Operation -eq 'Screenshots') {
            $ArgumentList=$requestedArguments+@('--apple-source-commit',$latest)
            Reject {Get-ForwardedArgumentList -SourceCommit $captured} 'must match the exact consumer HEAD'
            $ArgumentList=$requestedArguments+@('--apple-source-commit',$captured,'--apple-source-commit='+$captured)
            Reject {Get-ForwardedArgumentList -SourceCommit $captured} 'at most once'
        } else {
            $ArgumentList=$requestedArguments+@('--source-commit',$latest)
            Reject {Assert-SafeArguments} 'must match the exact consumer HEAD'
            $ArgumentList=$requestedArguments+@('--source-commit',$captured,'--SOURCE-COMMIT='+$captured)
            Reject {Assert-SafeArguments} 'Duplicate option'
        }
        $ArgumentList=@($command,$Operation,$configOption,$config)
        Reject {Assert-ConsumerSourceRepository} 'does not match origin/main'
        foreach($action in @('Ship','Advance','Prepare','Release')) {
            $ArgumentList=@('apple-release',$action,'--config','powerforge.release.json','--capture-provenance',$provenancePath)
            Reject {Assert-ConsumerSourceRepository} 'does not match origin/main'
        }
        Invoke-GitText -Root $consumer -Arguments @('checkout','--quiet','-B','main',$latest) | Out-Null
        $ArgumentList=@('apple-release','Ship','--config','powerforge.release.json')
        if((Assert-ConsumerSourceRepository) -ne $latest){throw 'Latest main Ship admission changed.'}
        'SHIP_LATEST_MAIN_UNCHANGED'
        Invoke-GitText -Root $consumer -Arguments @('checkout','--quiet','-B','main',$captured) | Out-Null
        [IO.File]::WriteAllText((Join-Path $consumer 'feature.txt'),'unmerged feature source')
        $unmerged=CommitFixture 'Unmerged local source'
        $ArgumentList=$requestedArguments
        Reject {Assert-ConsumerSourceRepository} 'Git failed'
        'UNMERGED_SOURCE_BLOCKED'
        """;
}
