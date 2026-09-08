using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeStringConcatenation_PreservesScopeOrderCollectionsAndErrorContinuation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-NativeConcatenation {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1, [object]$Token, [object]$Tail)
                $copy='before'
                $OFS=':'
                return '['+$Token+'|'+$Tail+'|'+$copy+']'
            }
            function Read-NativeConcatenationFailure {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1, [object]$Token)
                $copy='kept'
                $copy='['+$Token+']'
                return "after=$copy;status=$?"
            }
            function New-NativeConcatenationToken {
                $token=[pscustomobject]@{}
                Add-Member -InputObject $token -MemberType ScriptMethod -Name ToString -Force -Value {
                    $before=(Get-Variable -Scope 1 -Name copy).Value
                    Set-Variable -Scope 1 -Name copy -Value 'changed'
                    Set-Variable -Scope 1 -Name OFS -Value '/'
                    return "seen=$before"
                }
                $token
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeStringConcatenation", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.Equal(2, result.Manifest.UnitDispositionLedger!.Entries.Count(unit => unit.UsesNativeFunctionBinding));
        Assert.DoesNotContain(result.Manifest.UnitDispositionLedger.Entries, unit => unit.EmittedClrMethod && unit.RetainedHostedSource);
        const string probe = """
            $token=New-NativeConcatenationToken
            Read-NativeConcatenation -Token $token -Tail @('a','b')
            foreach ($item in @($null, [Management.Automation.Internal.AutomationNull]::Value, '', 12.5,
                [psobject]12.5, [char[]]'ab', @('a','b'), [Collections.ArrayList]@('a','b'))) {
                Read-NativeConcatenation -Token $item -Tail 'tail'
            }
            Add-Type -TypeDefinition 'public sealed class NativeConcatFailure { public override string ToString() { throw new System.InvalidOperationException("conversion failed"); } }'
            $failure=[NativeConcatFailure]::new()
            foreach ($action in 'Continue','SilentlyContinue','Stop','Ignore') {
                $Error.Clear(); $faults=@(); $records=@(); $caught=$null
                try { $records=@(Read-NativeConcatenationFailure -Token $failure -ErrorAction $action -ErrorVariable faults 2>&1) }
                catch { $caught=$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName }
                [pscustomobject]@{action=$action;caught=$caught;records=@($records | ForEach-Object {
                    if ($_ -is [Management.Automation.ErrorRecord]) { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName } else { $_ }
                });faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId });errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId })} |
                    ConvertTo-Json -Compress -Depth 6
            }
            Read-NativeConcatenationFailure -Token $failure -ErrorAction Continue 2>$null
            Read-NativeConcatenation -Token plain -Tail next
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-concatenation-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-concatenation-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("[seen=before|a/b|changed]", original.StandardOutput, StringComparison.Ordinal);
        Assert.True(original.StandardOutput.Contains("after=kept;status=False", StringComparison.Ordinal), original.StandardOutput);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
