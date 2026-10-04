using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeDeclarations_CompileConstraintsAndPreserveFailureContinuation(string framework, string host)
    {
        const string source = """
            function Invoke-NativeDeclaration {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value)
                [int]$x=$Seed
                $result=([int]$x += $Value)
                "result=$result;stored=$x"
                Get-Variable x | ForEach-Object { $_.Value.GetType().FullName; $_.Attributes | ForEach-Object { $_.GetType().FullName } }
                $x='bad'
                "after=$x"
            }
            function Invoke-NativeValidation {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value)
                [ValidateRange(1,9)][int]$x=$Seed
                [ValidateRange(1,3)][int]$x=$Value
                "retained=$x"
                [string]$x='replaced'
                "after=$x"
            }
            function Invoke-NativeDeclarationOrder {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value)
                $Trace=''
                [string]$x=$Value
                [int]$x += (Write-Output 3 -OutVariable Trace)
                "stored=$x;trace=$Trace"
            }
            function Invoke-NativeTypedMutation {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value)
                [int]$x=$Value
                $x++
                "after=$x;status=$?"
            }
            function Invoke-NativeSpecialAssignment {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value)
                $script:Stored=$Value
                $local:Stored=$Seed
                "script=$script:Stored;local=$local:Stored"
                $true=$Value
                "after=$true;status=$?"
            }
            function Invoke-NativeDiscardMutation {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value)
                $null += $Value
                $null -= $Value
                $null *= $Value
                $null /= 0
                $null %= 0
                "after=$?"
            }
            function Invoke-NativeCompoundSpecial {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value)
                $script:Stored=$Seed
                $script:Stored += $Value
                "script=$script:Stored"
                $true += $Value
                "after=$true"
            }
            function Invoke-NativeMultilineDeclaration {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value)
                $result=(
                    [ValidateRange(
                        1,3
                    )][int]$x=$Value
                )
                "stored=$x;result=$result"
            }
            Export-ModuleMember -Function Invoke-NativeDeclaration,Invoke-NativeValidation,Invoke-NativeDeclarationOrder,Invoke-NativeTypedMutation,Invoke-NativeSpecialAssignment,Invoke-NativeDiscardMutation,Invoke-NativeCompoundSpecial,Invoke-NativeMultilineDeclaration
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeDeclarations", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 8, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(item => item.DiagnosticChain.Select(cause => item.Name + ": " + cause.Message))));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit =>
        {
            Assert.True(unit.EmittedClrMethod);
            Assert.False(unit.RetainedHostedSource);
        });
        const string probe = """
            foreach($name in 'Invoke-NativeDeclaration','Invoke-NativeValidation','Invoke-NativeDeclarationOrder','Invoke-NativeTypedMutation','Invoke-NativeSpecialAssignment','Invoke-NativeDiscardMutation','Invoke-NativeCompoundSpecial','Invoke-NativeMultilineDeclaration') {
                foreach($value in 3,8,'bad',2147483647) {
                    foreach($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                        $Error.Clear(); $records=@(); $caught=$null
                        if($preference -eq 'Stop') {
                            try { $records=@(& $name -Value $value -ErrorAction $preference 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId }
                        } else {
                            $records=@(& $name -Value $value -ErrorAction $preference 2>$null)
                        }
                        [pscustomobject]@{name=$name;value=$value;preference=$preference;records=$records;caught=$caught;
                            errors=@($Error | ForEach-Object { [ordered]@{id=$_.FullyQualifiedErrorId;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;source=$_.InvocationInfo.Line;position=$_.InvocationInfo.PositionMessage} })} | ConvertTo-Json -Depth 12 -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-declarations-probe");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-declarations-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(128, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(6).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
