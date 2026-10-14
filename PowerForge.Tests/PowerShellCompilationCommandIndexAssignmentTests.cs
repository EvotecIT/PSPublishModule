using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCommandIndex_AssignmentPreservesRhsFirstLookupAndFailureState(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-AssignmentKey {
                [CmdletBinding()]
                param($State, [switch] $Fail, [switch] $NullKey)
                $State.Log.Add('key')
                if ($Fail) { throw 'key-failure' }
                if ($NullKey) { return $null }
                $State.Key
            }
            function Read-AssignmentValue {
                [CmdletBinding()]
                param($State)
                $State.Log.Add('rhs')
                $State.Key = 'after'
                'replacement'
            }
            function Read-CommandIndexAssignment {
                [CmdletBinding()]
                param([switch] $Fail, [switch] $NullKey)
                $state = [pscustomobject]@{Key='before';Log=[System.Collections.Generic.List[string]]::new()}
                $map = @{before='original';after='prior'}
                $errorId = ''
                $errorLine = 0
                try {
                    $map[(Read-AssignmentKey -State $state -Fail:$Fail -NullKey:$NullKey)] = (Read-AssignmentValue -State $state)
                } catch {
                    $errorId = $_.FullyQualifiedErrorId
                    $errorLine = $_.InvocationInfo.ScriptLineNumber
                } finally { $state.Log.Add('finally') }
                [pscustomobject]@{Before=$map.before;After=$map.after;Log=@($state.Log);ErrorId=$errorId;ErrorLine=$errorLine}
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CommandIndexAssignment",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, entry => Assert.False(entry.RetainedHostedSource));
        const string probe = """
            Read-CommandIndexAssignment | ConvertTo-Json -Depth 5 -Compress
            Read-CommandIndexAssignment -Fail | ConvertTo-Json -Depth 5 -Compress
            Read-CommandIndexAssignment -NullKey | ConvertTo-Json -Depth 5 -Compress
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-command-index");
        var generated = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "generated-command-index");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, generated.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(generated.StandardError), generated.StandardError);
        Assert.Equal(original.StandardOutput, generated.StandardOutput);
        Assert.Contains("\"Log\":[\"rhs\",\"key\",\"finally\"]", generated.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"After\":\"replacement\"", generated.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("key-failure", generated.StandardOutput, StringComparison.Ordinal);
    }
}
