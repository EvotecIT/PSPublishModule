using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeTypedArrayCapture_RemainsRejectedWithoutNativeHost(string framework)
    {
        using var fixture = ArtifactFixture.Create("function Read-Capture { param([int]$Value) [string[]]$Items=@(foreach($item in $Value){'literal'}); $Items }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Strict, targetFramework: framework,
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeTypedArrayCapture_PreservesConstraintsConversionErrorsAndFallback(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-ClosedStringCapture {
                [CmdletBinding()] param([int[]]$Values)
                [string[]]$Target=@(foreach($item in $Values){if($item -gt 0){'positive'}else{'zero'}})
                $Target
            }
            function Read-ClosedIntegerCapture {
                [CmdletBinding()] param([int[]]$Values)
                [int[]]$Target=@(foreach($item in $Values){if($item -gt 0){1}else{0}})
                $Target
            }
            function Read-StringCapture {
                [CmdletBinding()] param([object[]]$Values)
                [string[]]$Target='old'
                [string[]]$Target=@(foreach($item in $Values){$item})
                [pscustomobject]@{type=$Target.GetType().FullName;count=$Target.Count;values=@($Target)}
            }
            function Read-IntegerCapture {
                [CmdletBinding()] param([object[]]$Values,[switch]$ThrowAfterOutput)
                [int[]]$Target=7
                try {
                    [int[]]$Target=@(foreach($item in $Values){$item;if($ThrowAfterOutput){throw 'capture failed'}})
                } catch {
                    [pscustomobject]@{error=$_.FullyQualifiedErrorId;message=$_.Exception.Message;exception=$_.Exception.GetType().FullName;
                        line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;source=$_.InvocationInfo.Line}
                } finally { 'finally' }
                [pscustomobject]@{type=$Target.GetType().FullName;count=$Target.Count;values=@($Target)}
            }
            function Read-ExistingConstraint {
                [CmdletBinding()] param([object[]]$Values)
                [int[]]$Target=7
                try { $Target=@(foreach($item in $Values){$item}) }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
                $Target
            }
            function Read-ParameterConstraint {
                [CmdletBinding()] param([object[]]$Values,[int[]]$Target=7)
                try { $Target=@(foreach($item in $Values){$item}) }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
                $Target
            }
            function Read-LocalConstraint {
                [CmdletBinding()] param([object[]]$Values)
                [int[]]$Target=7
                try { $local:Target=@(foreach($item in $Values){$item}) }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
                $Target
            }
            function Read-LocalDeclaration {
                [CmdletBinding()] param([object[]]$Values)
                [int[]]$local:Target=7
                try { $Target=@(foreach($item in $Values){$item}) }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
                $Target
            }
            function Read-TypedCatch {
                [CmdletBinding()] param([object[]]$Values)
                [int[]]$Target=7
                try { $Target=@(foreach($item in $Values){$item}) }
                catch [System.Management.Automation.PSInvalidCastException] { 'cast'; $_.Exception.GetType().FullName }
                catch { 'all'; $_.Exception.GetType().FullName }
                finally { 'finally' }
                $Target
            }
            function Read-MixedCatch {
                [CmdletBinding()] param([object[]]$Values)
                [int[]]$Target=7
                try { $Target=@(foreach($item in $Values){$item}) }
                catch [InvalidOperationException] { 'invalid' }
                catch { 'all'; $_.Exception.GetType().FullName }
                $Target
            }
            function Read-MethodCatch {
                [CmdletBinding()] param([string]$Text)
                try { [int]::Parse($Text) }
                catch [InvalidOperationException] { 'invalid' }
                catch { 'all'; $_.Exception.GetType().FullName; $_.FullyQualifiedErrorId }
            }
            function Read-BlockedCapture {
                [CmdletBinding()] param([int]$Value)
                [string[]]$Items=@(foreach($item in $Value){[string]$item})
                function Read-Nested { 'nested' }
                $Items
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeTypedArrayCapture",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Read-ClosedStringCapture", "Read-ClosedIntegerCapture" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        }
        foreach (var name in new[] { "Read-StringCapture", "Read-IntegerCapture", "Read-ExistingConstraint", "Read-ParameterConstraint", "Read-LocalConstraint", "Read-LocalDeclaration", "Read-TypedCatch", "Read-MixedCatch", "Read-MethodCatch" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        }
        var blocked = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == "Read-BlockedCapture");
        Assert.True(blocked.RetainedHostedSource);
        Assert.False(blocked.EmittedClrMethod);
        Assert.NotEmpty(blocked.GeneratedRegionMemberNames);
        const string probe = """
            foreach($name in 'Read-ClosedStringCapture','Read-ClosedIntegerCapture') {
                foreach($values in @(@(),@(0),@(1),@(0,1,2))) {
                    [pscustomobject]@{name=$name;values=$values;records=@(& $name -Values $values)}|ConvertTo-Json -Depth 6 -Compress
                }
            }
            foreach($name in 'Read-ExistingConstraint','Read-ParameterConstraint','Read-LocalConstraint','Read-LocalDeclaration','Read-TypedCatch','Read-MixedCatch') {
                [pscustomobject]@{name=$name;records=@(& $name -Values @(1,'bad',3))}|ConvertTo-Json -Depth 6 -Compress
            }
            [pscustomobject]@{methodRecords=@(Read-MethodCatch -Text 'bad')}|ConvertTo-Json -Depth 6 -Compress
            & (Get-Command Read-StringCapture).Module {
                $script:trace=[System.Collections.Generic.List[string]]::new()
                $callback=[pscustomobject]@{Value='callback'}
                $callback|Add-Member ScriptMethod ToString {
                    $prior=Get-Variable Target -ValueOnly
                    $script:trace.Add('seen:'+($prior-join '|'))
                    $this.Value
                } -Force
                foreach($case in 'empty','null','one','many','callback') {
                    $values=switch($case){empty {,@()} null {,@($null)} one {,@(42)} many {,@(1,'two',$null)} callback {,@($callback,$callback)}}
                    $observed=Read-StringCapture -Values $values
                    [pscustomobject]@{case=$case;result=$observed;trace=@($script:trace)}|ConvertTo-Json -Compress -Depth 6
                }
                foreach($case in 'empty','one','many','invalid','throw') {
                    $values=switch($case){empty {,@()} one {,@('12')} many {,@(1,'2',3)} invalid {,@(1,'bad',3)} throw {,@(1,2)}}
                    [pscustomobject]@{case=$case;records=@(Read-IntegerCapture -Values $values -ThrowAfterOutput:($case -eq 'throw'))}|ConvertTo-Json -Compress -Depth 6
                }
                [pscustomobject]@{blocked=@(Read-BlockedCapture -Value 5)}|ConvertTo-Json -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("seen:old", generated);
        Assert.Contains("capture failed", generated);
        Assert.Contains("\"values\":[7]", generated);
    }
}
