using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void LiteralTryValue_PreservesRecordsOrderRollbackAndCatchFinally(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-LiteralTry {
                [CmdletBinding()]param([int]$Case,$Trace)
                $value='previous'
                try {
                    $value=[ordered]@{
                        Before=$Trace.Add('before')
                        Value=try {
                            $Trace.Add('try')
                            if($Case -eq 1){$null}
                            if($Case -eq 2){7}
                            if($Case -ge 3){7;8}
                            if($Case -eq 4 -or $Case -eq 5){throw 'failure'}
                        } catch {
                            $Trace.Add('catch')
                            if($Case -eq 5){throw}
                            [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                        } finally {$Trace.Add('finally');if($Case -eq 6){'tail'}}
                        After=$Trace.Add('after')
                    }
                } catch {[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                [pscustomobject]@{value=$value;trace=@($Trace.ToArray());itemNames=$(if($value -is [Collections.IDictionary]){@($value.Keys)})}
            }
            function Read-CommandTry { [CmdletBinding()]param([int]$Case) [pscustomobject]@{Value=try{Write-Output 1,2;if($Case -eq 1){throw 'failure'}}catch{'caught'}finally{'tail'}} }
            function Read-DiscardTry {param();[pscustomobject]@{Value=try {@([void][int]::Parse('1');'ok')}finally{}}}
            function Read-ObjectTry {
                [CmdletBinding()]param([string]$Text)
                [pscustomobject]@{Flag=try {[bool]::Parse($Text)}catch{$null};Nested=try {[ordered]@{Inner=try {$Text}finally{'tail'}}}catch{$null}}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.LiteralTryValue", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(4, built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Kind == PowerShellCompilationUnitKind.Function), unit =>
        {
            Assert.True(unit.EmittedClrMethod);
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        });
        const string probe = """
            [pscustomobject]@{kind='discard';records=@(Read-DiscardTry)}|ConvertTo-Json -Depth 12 -Compress
            foreach($case in 0,1,2,3,4,5,6,0) {
                $trace=[Collections.Generic.List[string]]::new()
                [pscustomobject]@{kind='map';case=$case;records=@(Read-LiteralTry -Case $case -Trace $trace)}|ConvertTo-Json -Depth 12 -Compress
            }
            foreach($case in 0,1){[pscustomobject]@{kind='command';records=@(Read-CommandTry -Case $case)}|ConvertTo-Json -Depth 12 -Compress}
            foreach($text in 'True','False','bad',$null,'True') {
                [pscustomobject]@{kind='object';text=$text;records=@(Read-ObjectTry -Text $text)}|ConvertTo-Json -Depth 12 -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(16, original.Length);
        Assert.Equal(original.Length, generated.Length);
        for (var i = 0; i < original.Length; i++)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original[i]), JsonNode.Parse(generated[i])),
                "Original: " + original[i] + Environment.NewLine + "Generated: " + generated[i]);
        Assert.Contains("previous", generated[6]);
        Assert.DoesNotContain("after", generated[6]);
        Assert.Contains("tail", generated[7]);
    }
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void LiteralTryValue_PreservesInnerSourceMapsAndClosedTransferTypeStrictBoundaries(string framework)
    {
        var source = PowerShellSourceParser.Parse("""
            function Read-Value {
                param([string]$Text)
                [pscustomobject]@{Flag=try {
                    [bool]::Parse($Text)
                } catch {$null}}
            }
            function Read-Return {param();[pscustomobject]@{Value=try{return 'x'}catch{'caught'}}}
            function Read-Loop {param();foreach($item in 1,2){[pscustomobject]@{Value=try{continue}finally{'tail'}}}}
            function Read-Type {param();[pscustomobject]@{Value=try{'x'}catch [Missing.Authored.Exception]{'caught'}}}
            """, Path.Combine(Path.GetTempPath(), "literal-try-boundaries.ps1"));
        var hybrid = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.HybridModule);
        var method = Assert.Single(hybrid.Emitted.Methods, item => item.GeneratedName == "Read_Value");
        Assert.NotNull(method.NativeFunctionBinding);
        Assert.Contains(method.SourceMap, entry => entry.SourceStartLine == 4 && entry.GeneratedStartLine > 0);
        Assert.DoesNotContain(hybrid.Emitted.Methods, item => item.GeneratedName is "Read_Return" or "Read_Loop" or "Read_Type");
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Empty(strict.Emitted.Methods);
    }

}
