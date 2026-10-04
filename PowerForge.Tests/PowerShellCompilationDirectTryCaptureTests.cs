using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void DirectTryCapture_PreservesCardinalityRollbackReturnAndAccessConversion(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-DirectTry {
                param([int]$Case)
                $value='previous'
                try {
                    $value=try {
                        if($Case -eq 1){$null}
                        if($Case -eq 2){'one'}
                        if($Case -ge 3){'first';'second'}
                        if($Case -eq 4 -or $Case -eq 5){throw 'inner'}
                        if($Case -eq 6){return 'returned'}
                    } catch {
                        if($Case -eq 5){throw}
                        'caught';$_.FullyQualifiedErrorId
                    } finally {if($Case -gt 2){'finally'}}
                    'after'
                } catch {
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                }
                [pscustomobject]@{value=$value;isNull=$null -eq $value;type=$(if($null -ne $value){$value.GetType().FullName})}
            }
            function Read-TryAccess {
                param($Map,$InputValue,$Trace)
                try {
                    [bool]$Map['flag']=try { [bool]::Parse($InputValue) } catch {$Trace.Add('caught');$null} finally {$Trace.Add('finally')}
                } catch { [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine} }
                $Map
            }
            function Read-TryTyped {
                param($InputValue)
                [int[]]$value=7
                try { $value=try {$InputValue} finally {8} }
                catch {[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                ,$value
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.DirectTryCapture", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(3, built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Kind == PowerShellCompilationUnitKind.Function), unit =>
        {
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        });
        const string probe = """
            foreach($case in 0,1,2,3,4,5,6,0) {
                [pscustomobject]@{kind='records';case=$case;records=@(Read-DirectTry -Case $case)}|ConvertTo-Json -Depth 8 -Compress
            }
            foreach($inputValue in 'True','False','bad',$null,'True') {
                foreach($map in ([ordered]@{flag='previous'}),$null) {
                    $trace=[Collections.Generic.List[string]]::new()
                    [pscustomobject]@{kind='access';input=$inputValue;records=@(Read-TryAccess -Map $map -InputValue $inputValue -Trace $trace);trace=@($trace.ToArray());map=$map}|ConvertTo-Json -Depth 8 -Compress
                }
            }
            foreach($inputValue in $null,1,([object[]]@(2,3)),'bad',1) {
                [pscustomobject]@{kind='typed';input=$inputValue;records=@(Read-TryTyped -InputValue $inputValue)}|ConvertTo-Json -Depth 8 -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(23, original.Length);
        Assert.Equal(original.Length, generated.Length);
        for (var i=0;i<original.Length;i++)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original[i]),JsonNode.Parse(generated[i])), "Original: "+original[i]+Environment.NewLine+"Generated: "+generated[i]);
        Assert.Contains("previous", generated[5]);
        Assert.Contains("returned", generated[6]);
        Assert.DoesNotContain("after", generated[6]);
        Assert.Equal("System.String", JsonNode.Parse(generated[2])!["records"]![1]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void DirectTryCapture_KeepsEscapingLoopsUnavailableTypesAndStrictClosed(string framework)
    {
        var source=PowerShellSourceParser.Parse("""
            function Read-Try {param();$value=try {'ok'} catch {'caught'};$value}
            function Read-Escape {param();foreach($item in 1,2){$value=try {'partial';continue}finally{'finally'};$value}}
            function Read-Type {param();$value=try {'ok'} catch [Missing.Authored.Exception] {'caught'};$value}
            """, Path.Combine(Path.GetTempPath(), "direct-try-negative.ps1"));
        var hybrid=new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,PowerShellCompilationCapabilities.HybridModule);
        Assert.Contains(hybrid.Emitted.Methods, method=>method.GeneratedName=="Read_Try" && method.NativeFunctionBinding is not null);
        Assert.DoesNotContain(hybrid.Emitted.Methods, method=>method.GeneratedName is "Read_Escape" or "Read_Type");
        var strict=new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Empty(strict.Emitted.Methods);
    }
}
