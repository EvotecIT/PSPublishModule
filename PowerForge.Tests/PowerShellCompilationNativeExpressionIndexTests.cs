using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeExpressionIndexes_PreserveEvaluationAndOfflineTranspose(string framework, string host)
    {
        var transpose = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Objects", "Format-TransposeTable.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(transpose) + Environment.NewLine + """
            function Write-ExpressionIndex {
                [CmdletBinding()]param([object]$Map,[object]$Selector,[object]$Value,[string]$Mode,[object]$Trace)
                $marker='local'
                try {
                    if($Mode -eq 'compound') { $Map[$Selector.Left + $Selector.Right] += $Value.GetValue() }
                    elseif($Mode -eq 'increment') { $result=$Map[($Selector.Left + $Selector.Right)]++; $result }
                    elseif($Mode -eq 'subtract') { $Map[$Selector.Left - $Selector.Right]=$Value.GetValue() }
                    elseif($Mode -eq 'multiply') { $Map[$Selector.Left * $Selector.Right]=$Value.GetValue() }
                    elseif($Mode -eq 'divide') { $Map[$Selector.Left / $Selector.Right]=$Value.GetValue() }
                    elseif($Mode -eq 'remainder') { $Map[$Selector.Left % $Selector.Right]=$Value.GetValue() }
                    elseif($Mode -eq 'unary') { $Map[-($Selector.Left + $Selector.Right)]=$Value.GetValue() }
                    elseif($Mode -eq 'subexpression') { $Map[$($Selector.Left)]=$Value.GetValue() }
                    elseif($Mode -eq 'compoundSubexpression') { $Map[$($Selector.Left)] += $Value.GetValue() }
                    elseif($Mode -eq 'subexpressionRhs') { $Map[$($Selector.Left)] = $($Value.GetValue()) }
                    elseif($Mode -eq 'compoundSubexpressionRhs') { $Map[$($Selector.Left)] += $($Value.GetValue()) }
                    else { $Map[$Selector.Left + $Selector.Right]=$Value.GetValue() }
                } catch { 'error:'+ $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.CategoryInfo.Category+':'+$_.InvocationInfo.OffsetInLine }
                finally { $Trace.Add('finally') }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ExpressionIndexes", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Format-TransposeTable", "Write-ExpressionIndex" })
            Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name && unit.EmittedClrMethod && unit.UsesNativeFunctionBinding);
        const string probe = """
            foreach($left in 2,'2','name',$null) {
                foreach($right in 1,0,'bad') {
                    foreach($mode in 'assign','compound','increment','subtract','multiply','divide','remainder','unary','subexpression') {
                        $trace=[Collections.Generic.List[string]]::new()
                        $selector=[pscustomobject]@{A=$left;B=$right;Trace=$trace}
                        $selector | Add-Member ScriptProperty Left {$this.Trace.Add('left:'+ $marker);$this.A}
                        $selector | Add-Member ScriptProperty Right {$this.Trace.Add('right:'+ $marker);$this.B}
                        $value=[pscustomobject]@{Trace=$trace}
                        $value | Add-Member ScriptMethod GetValue {$this.Trace.Add('rhs:'+ $marker);7}
                        $map=@{3=10;'21'=10;'name1'=10}
                        $result=@(Write-ExpressionIndex -Map $map -Selector $selector -Value $value -Mode $mode -Trace $trace)
                        $keys=@($map.Keys|ForEach-Object {$_.GetType().FullName+':'+$_+'='+$map[$_]}|Sort-Object)
                        [pscustomobject]@{left=$left;right=$right;mode=$mode;result=$result;trace=@($trace);keys=$keys}|ConvertTo-Json -Depth 5 -Compress
                    }
                }
            }
            foreach($mode in 'assign','compound','subexpression','compoundSubexpression','subexpressionRhs','compoundSubexpressionRhs') {
                $trace=[Collections.Generic.List[string]]::new()
                $selector=[pscustomobject]@{A=2;B=1;Trace=$trace}
                $selector | Add-Member ScriptProperty Left {$this.Trace.Add('left');$this.A}
                $selector | Add-Member ScriptProperty Right {$this.Trace.Add('right');$this.B}
                $value=[pscustomobject]@{Trace=$trace}
                $value | Add-Member ScriptMethod GetValue {$this.Trace.Add('rhs');throw 'rhs failed'}
                $map=@{2=10;3=10}
                $result=@(Write-ExpressionIndex -Map $map -Selector $selector -Value $value -Mode $mode -Trace $trace)
                [pscustomobject]@{mode=$mode;result=$result;trace=@($trace);two=$map[2];three=$map[3]}|ConvertTo-Json -Depth 5 -Compress
            }
            $objects=@([pscustomobject]@{Name='one';Value=1;Flag=$false},[pscustomobject]@{Name='two';Value=0;Flag=$true})
            $maps=@([ordered]@{Name='one';Value=1;Flag=$false},[ordered]@{Name='two';Value=0;Flag=$true})
            foreach($inputObjects in $objects,$maps) {
                foreach($legacy in $false,$true) {
                    foreach($sort in 'ASC','DESC','NONE') {
                        $parameters=@{AllObjects=$inputObjects}
                        if($legacy) { $parameters.Legacy=$true;$parameters.Sort=$sort } else { $parameters.Property='Name' }
                        $direct=@(Format-TransposeTable @parameters)
                        $parameters.Remove('AllObjects')
                        $pipeline=@($inputObjects|Format-TransposeTable @parameters)
                        [pscustomobject]@{legacy=$legacy;sort=$sort;direct=$direct;pipeline=$pipeline}|ConvertTo-Json -Depth 8 -Compress
                    }
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"rhs:\"", generated);
        Assert.Contains("\"left:\"", generated);
    }

    [Theory]
    [InlineData("$Map[$(Get-Date)]=1")]
    [InlineData("$Map[$($Key;1)]=1")]
    [InlineData("$Map[($Key.GetValue()+1)]=1")]
    [InlineData("$Map[$($Key > 'output')]=1")]
    public void NativeExpressionIndexes_CommandOrBodyKeysRemainHosted(string body)
    {
        using var fixture = ArtifactFixture.Create("function Set-Index { [CmdletBinding()]param([object]$Map,[object]$Key); "+body+" }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(new[] { fixture.ScriptPath },
            "Generated.ExpressionIndexBoundary", "CompiledPowerShell", "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Methods);
    }
}
