namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Theory]
    [InlineData("Status")]
    [InlineData("TestFlight")]
    [InlineData("SubmitTestFlightReview")]
    public void PinnedExistingBetaRecoveryPreservesExactMainAncestor(string operation)
    {
        if (OperatingSystem.IsWindows()) return; // Signing Mac operator uses PowerShell 7.
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"powerforge-existing-beta-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(parent);
            var harness = Path.Combine(parent, "existing-beta.ps1");
            File.WriteAllText(harness, ExistingBetaSourcePolicyHarness);
            var result = Run("pwsh", parent, "-NoLogo", "-NoProfile", "-File", harness,
                "-Helper", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.ps1"),
                "-Fixture", parent, "-Operation", operation);
            result.EnsureSuccess();
            Assert.Contains("EXACT_BETA_ANCESTOR_PASS", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("UNSAFE_SOURCE_BLOCKED", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("NEW_ARTIFACT_LATEST_MAIN_UNCHANGED", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("EXPLICIT_RECOVERY_SOURCE_REQUIRED", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("LATEST_MAIN_IMPLICIT_SOURCE_UNCHANGED", result.StandardOutput, StringComparison.Ordinal);
        }
        finally { DeletePinnedEvidenceDirectory(parent); }
    }

    private const string ExistingBetaSourcePolicyHarness = """
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
        $requiredBranch='main'
        $ExpectedConsumerRepository='Synthetic/BetaFixture'
        $script:allowedConsumerEvidencePaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        New-Item -ItemType Directory -Path $consumer -Force | Out-Null
        # Substitute only GitHub transport identity. Branch, fresh fetch, ancestry,
        # tracked inputs, source forwarding and content validation remain real.
        function Assert-GitHubOrigin {
            param([string]$Root,[string]$ExpectedRepository)
            if($ExpectedRepository -ne 'Synthetic/BetaFixture' -or
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
        [IO.File]::WriteAllText((Join-Path $consumer 'source.txt'),'uploaded application source')
        [IO.File]::WriteAllText((Join-Path $consumer 'powerforge.release.json'),'{"AppleApps":{"ProjectRoot":".","Apps":[{"Name":"App","Platform":"IOS","BundleId":"com.example.beta-fixture","AppStoreConnectAppId":"123"}]}}')
        $uploaded=CommitFixture 'Uploaded build source'
        Invoke-GitText -Root $Fixture -Arguments @('clone','--quiet','--bare','--no-hardlinks',$consumer,$origin) | Out-Null
        Invoke-GitText -Root $consumer -Arguments @('remote','add','origin',$origin) | Out-Null
        [IO.File]::WriteAllText((Join-Path $consumer 'tooling.txt'),'later dependency maintenance')
        $latest=CommitFixture 'Newer main'
        Invoke-GitText -Root $consumer -Arguments @('push','--quiet','origin','main') | Out-Null
        $ArgumentList=@('apple-release',$Operation,'--config','powerforge.release.json')
        $current=Assert-ConsumerSourceRepository
        $implicit=@(Get-ForwardedArgumentList -SourceCommit $current)
        if($current -ne $latest -or $implicit[-2] -ne '--apple-source-commit' -or $implicit[-1] -ne $latest){throw 'Latest main implicit source changed.'}
        'LATEST_MAIN_IMPLICIT_SOURCE_UNCHANGED'
        Invoke-GitText -Root $consumer -Arguments @('checkout','--quiet','-B','main',$uploaded) | Out-Null
        Invoke-GitText -Root $consumer -Arguments @('update-ref','refs/remotes/origin/main',$uploaded) | Out-Null
        $requested=@('apple-release',$Operation,'--config','powerforge.release.json','--apple-source-commit',$uploaded)
        $ArgumentList=$requested
        $admitted=Assert-ConsumerSourceRepository
        if($admitted -ne $uploaded -or (Invoke-GitText -Root $consumer -Arguments @('rev-parse','origin/main')) -ne $latest){throw 'Recovery source was not checked against freshly fetched main.'}
        Assert-SafeArguments
        Assert-ConsumerRepositoryContent
        $forwarded=@(Get-ForwardedArgumentList -SourceCommit $admitted)
        if(($forwarded -join '|') -ne ($requested -join '|')){throw 'Uploaded source identity was relabeled.'}
        $ArgumentList=@('apple-release',$Operation,'--config','powerforge.release.json',"--apple-source-commit=$($uploaded.ToUpperInvariant())")
        $equalSource=Assert-ConsumerSourceRepository
        $equalForwarded=@(Get-ForwardedArgumentList -SourceCommit $equalSource)
        if(($equalForwarded -join '|') -ne ($requested -join '|')){throw 'Equals-form source identity was not retained.'}
        'EXACT_BETA_ANCESTOR_PASS'

        $ArgumentList=@('apple-release',$Operation,'--config','powerforge.release.json')
        Reject {Assert-ConsumerSourceRepository} 'does not match origin/main'
        foreach($invalidSource in @('', ' ', $uploaded.Substring(0,8), $latest)) {
            $ArgumentList=@('apple-release',$Operation,'--config','powerforge.release.json',"--apple-source-commit=$invalidSource")
            Reject {Assert-ConsumerSourceRepository} 'does not match origin/main'
        }
        $ArgumentList=@('apple-release',$Operation,'--config','powerforge.release.json','--apple-source-commit')
        Reject {Assert-ConsumerSourceRepository} 'Missing value'
        $ArgumentList=$requested+@("--apple-source-commit=$uploaded")
        Reject {Assert-ConsumerSourceRepository} 'Duplicate option'
        'EXPLICIT_RECOVERY_SOURCE_REQUIRED'

        $ArgumentList=@('apple-release',$Operation,'--config','powerforge.release.json','--apple-source-commit',$latest)
        Reject {Get-ForwardedArgumentList -SourceCommit $admitted} 'must match the exact consumer HEAD'
        $ArgumentList=$requested
        [IO.File]::AppendAllText((Join-Path $consumer 'powerforge.release.json'),' ')
        Reject {Assert-SafeArguments} 'Git failed'
        Invoke-GitText -Root $consumer -Arguments @('checkout','--quiet','HEAD','--','powerforge.release.json') | Out-Null
        [IO.File]::WriteAllText((Join-Path $consumer 'unreviewed.txt'),'unreviewed input')
        Reject {Assert-ConsumerRepositoryContent} 'non-reviewed content'
        Remove-Item -LiteralPath (Join-Path $consumer 'unreviewed.txt')
        foreach($action in @('Configured','Version','Archive','Rehearse','Upload','UploadExisting','Ship','Advance','Prepare','Screenshots','SubmitAppReview','Release','Cleanup','Doctor')) {
            $ArgumentList=@('apple-release',$action,'--config','powerforge.release.json')
            Reject {Assert-ConsumerSourceRepository} 'does not match origin/main'
        }
        'NEW_ARTIFACT_LATEST_MAIN_UNCHANGED'
        Invoke-GitText -Root $consumer -Arguments @('checkout','--quiet','-b','feature',$uploaded) | Out-Null
        $ArgumentList=$requested
        Reject {Assert-ConsumerSourceRepository} 'must be on'
        Invoke-GitText -Root $consumer -Arguments @('checkout','--quiet','main') | Out-Null
        [IO.File]::WriteAllText((Join-Path $consumer 'feature.txt'),'unmerged application source')
        $unmerged=CommitFixture 'Unmerged local source'
        $ArgumentList=@('apple-release',$Operation,'--config','powerforge.release.json','--apple-source-commit',$unmerged)
        Reject {Assert-ConsumerSourceRepository} 'Git failed'
        'UNSAFE_SOURCE_BLOCKED'
        """;
}
