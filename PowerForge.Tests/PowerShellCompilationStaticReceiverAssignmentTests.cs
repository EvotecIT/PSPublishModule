using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_CultureScopeRestoresThreadStateAfterSuccessAndFailure(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSScriptTools", "Culture", "Test-WithCulture.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CultureScope", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "Test-WithCulture" && unit.EmittedClrMethod && unit.UsesNativeFunctionBinding && !unit.RetainedHostedSource);
        const string probe = """
            $originalCulture=[Threading.Thread]::CurrentThread.CurrentCulture
            $originalUICulture=[Threading.Thread]::CurrentThread.CurrentUICulture
            try {
                foreach($name in 'fr-FR','de-DE','tr-TR') {
                    foreach($fail in $false,$true) {
                      foreach($stop in $false,$true) {
                        $warnings=@()
                        $expectedCulture=$PSCulture
                        $expectedUI=$PSUICulture
                        $callback={
                            param($shouldFail)
                            [pscustomobject]@{culture=[Threading.Thread]::CurrentThread.CurrentCulture.Name;ui=[Threading.Thread]::CurrentThread.CurrentUICulture.Name;number=([double]1234.5).ToString('N1');argument=$shouldFail}
                            if($shouldFail) {throw 'offline culture failure'}
                        }
                        if($stop) {$result=@(Test-WithCulture -Culture $name -Scriptblock $callback -ArgumentList @($fail) -WarningAction SilentlyContinue -WarningVariable warnings | Select-Object -First 1)}
                        else {$result=@(Test-WithCulture -Culture $name -Scriptblock $callback -ArgumentList @($fail) -WarningAction SilentlyContinue -WarningVariable warnings)}
                        $current=[Threading.Thread]::CurrentThread
                        [pscustomobject]@{name=$name;fail=$fail;stop=$stop;records=$result;warnings=@($warnings|ForEach-Object {$_.Message});expectedCulture=$expectedCulture;expectedUI=$expectedUI;restoredCulture=$current.CurrentCulture.Name;restoredUI=$current.CurrentUICulture.Name}|ConvertTo-Json -Depth 6 -Compress
                      }
                    }
                }
            } finally {
                [Threading.Thread]::CurrentThread.CurrentCulture=$originalCulture
                [Threading.Thread]::CurrentThread.CurrentUICulture=$originalUICulture
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"culture\":\"fr-FR\"", generated);
        var observations = generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(12, observations.Length);
        foreach (var observation in observations)
        {
            var row = System.Text.Json.Nodes.JsonNode.Parse(observation)!;
            Assert.Equal(row["expectedCulture"]!.GetValue<string>(), row["restoredCulture"]!.GetValue<string>());
            Assert.Equal(row["expectedUI"]!.GetValue<string>(), row["restoredUI"]!.GetValue<string>());
        }
        Assert.Contains("offline culture failure", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeStaticReceiverAssignments_PreserveOrderStorageAndErrors(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-StaticAssignmentValue {
                [CmdletBinding()]param($Trace,$Value,[switch]$Fail)
                $Trace.Add('rhs')
                if($Fail){throw 'rhs failure'}
                return $Value
            }
            function Set-StaticReceiverValue {
                [CmdletBinding()]param($Trace,$Value,[string]$Operation,[switch]$Fail)
                try {
                    if($Operation -eq 'assign') { [System.Threading.Thread]::CurrentThread.OwnedReceiver.Value = Get-StaticAssignmentValue $Trace $Value -Fail:$Fail }
                    elseif($Operation -eq 'compound') { [System.Threading.Thread]::CurrentThread.OwnedReceiver.Value += Get-StaticAssignmentValue $Trace $Value -Fail:$Fail }
                    elseif($Operation -eq 'index') { [System.Threading.Thread]::CurrentThread.OwnedReceiver.Items[$Value] = Get-StaticAssignmentValue $Trace 7 -Fail:$Fail }
                    elseif($Operation -eq 'subindex') { [System.Threading.Thread]::CurrentThread.OwnedReceiver.Items[$($Value+1)] += Get-StaticAssignmentValue $Trace 7 -Fail:$Fail }
                    elseif($Operation -eq 'subassign') { [System.Threading.Thread]::CurrentThread.OwnedReceiver.Items[$($Value+1)] = Get-StaticAssignmentValue $Trace 7 -Fail:$Fail }
                } catch { [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;category=[string]$_.CategoryInfo.Category;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine} }
                finally { $Trace.Add('finally') }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StaticReceiverAssignment", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "Set-StaticReceiverValue" && unit.EmittedClrMethod && unit.UsesNativeFunctionBinding);
        const string probe = """
            Update-TypeData -TypeName System.Threading.Thread -MemberType ScriptProperty -MemberName OwnedReceiver -Value {
                $global:StaticAssignmentTrace.Add('receiver')
                if($global:StaticAssignmentMode -eq 'throw') {throw 'getter failure'}
                if($global:StaticAssignmentMode -eq 'null') {return $null}
                $global:StaticAssignmentHolder
            } -Force
            try {
                foreach($operation in 'assign','compound','index','subindex','subassign') {
                    foreach($mode in 'normal','null','throw','setter') {
                        foreach($fail in $false,$true) {
                            $global:StaticAssignmentTrace=[Collections.Generic.List[string]]::new()
                            $global:StaticAssignmentMode=$mode
                            $global:StaticAssignmentStored=5
                            $global:StaticAssignmentHolder=[pscustomobject]@{Items=@(1,2,3)}
                            $global:StaticAssignmentHolder|Add-Member -MemberType ScriptProperty -Name Value -Value {
                                $global:StaticAssignmentTrace.Add('read');$global:StaticAssignmentStored
                            } -SecondValue {
                                param($newValue)
                                $global:StaticAssignmentTrace.Add('write')
                                if($global:StaticAssignmentMode -eq 'setter'){throw 'setter failure'}
                                $global:StaticAssignmentStored=$newValue
                            }
                            $result=@(Set-StaticReceiverValue -Trace $global:StaticAssignmentTrace -Value 1 -Operation $operation -Fail:$fail -ErrorAction Stop)
                            [pscustomobject]@{operation=$operation;mode=$mode;fail=$fail;result=$result;stored=$global:StaticAssignmentStored;items=@($global:StaticAssignmentHolder.Items);trace=@($global:StaticAssignmentTrace)}|ConvertTo-Json -Depth 7 -Compress
                        }
                    }
                }
            } finally {Remove-TypeData -TypeName System.Threading.Thread}
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"stored\":6", generated);
        Assert.Contains("\"items\":[1,7,3]", generated);
        Assert.Contains("\"trace\":[\"receiver\",\"rhs\",\"write\",\"finally\"]", generated);
    }

    [Theory]
    [InlineData("[Missing.Authored.Type]::Receiver.Value = 1")]
    [InlineData("[System.Threading.Thread]::CurrentThread.($Name) = 1")]
    [InlineData("[System.Threading.Thread]::CurrentThread.GetType().Value = 1")]
    [InlineData("[System.Threading.Thread]::CurrentThread.Values[(Get-Date)] = 1")]
    public void NativeStaticReceiverAssignments_UnqualifiedTargetsRemainHosted(string assignment)
    {
        using var fixture = ArtifactFixture.Create("function Set-StaticValue { [CmdletBinding()]param($Name);"+assignment+" }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(new[] { fixture.ScriptPath },
            "Generated.StaticReceiverBoundary", "CompiledPowerShell", "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Methods);
    }

    [Fact]
    public void NativeStaticReceiverAssignments_DoNotWidenRuntimeFreeAdmission()
    {
        var document = PowerShellSourceParser.Parse("function Set-StaticValue { [Threading.Thread]::CurrentThread.CurrentCulture = [cultureinfo]'fr-FR' }",
            Path.Combine(Path.GetTempPath(), "static-receiver-strict.ps1"));
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(strict.Emitted.Methods);
    }
}
