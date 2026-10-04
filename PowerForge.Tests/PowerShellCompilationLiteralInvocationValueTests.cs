using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void LiteralLocalInvocations_PreserveCardinalityOrderIdentityAndFailures(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-InvocationMark { [CmdletBinding()]param($Trace,[string]$Mark);$Trace.Add($Mark);$Mark }
            function New-InvocationObject {
                [CmdletBinding()]param($Callback,$Arguments,$Trace)
                try { [pscustomobject]@{Before=Get-InvocationMark $Trace 'before';Result=& $Callback @Arguments;After=Get-InvocationMark $Trace 'after'} }
                finally { $Trace.Add('finally') }
            }
            function New-InvocationMap {
                [CmdletBinding()]param($Callback,$Arguments,$Trace)
                try {
                    $map=@{Before=Get-InvocationMark $Trace 'before';Result=& $Callback @Arguments;After=Get-InvocationMark $Trace 'after'}
                    [pscustomobject]@{Before=$map['Before'];Result=$map['Result'];After=$map['After']}
                }
                finally { $Trace.Add('finally') }
            }
            function New-InvocationOrderedMap {
                [CmdletBinding()]param($Callback,$Arguments,$Trace)
                try {
                    $map=[ordered]@{Before=Get-InvocationMark $Trace 'before';Result=& $Callback @Arguments;After=Get-InvocationMark $Trace 'after'}
                    [pscustomobject]@{Before=$map['Before'];Result=$map['Result'];After=$map['After']}
                }
                finally { $Trace.Add('finally') }
            }
            function Read-LiteralLocalClosure {
                [CmdletBinding()]param($Trace)
                $value='owner'
                $callback={param([int]$Count);$Trace.Add($value);$value='child';foreach($number in 1..$Count){$value+':'+$number}}
                [pscustomobject]@{Result=& $callback -Count 2;Owner=$value}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.LiteralLocalInvocation", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "New-InvocationObject", "New-InvocationMap", "New-InvocationOrderedMap", "Read-LiteralLocalClosure" })
            Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name && unit.EmittedClrMethod && unit.UsesNativeFunctionBinding);
        const string probe = """
            foreach($name in 'New-InvocationObject','New-InvocationMap','New-InvocationOrderedMap') {
                foreach($mode in 'zero','one','many','null','fail','partial','invalid') {
                    $trace=[Collections.Generic.List[string]]::new()
                    $item=[pscustomobject]@{name='shared'}
                    $callback={param($Mode,$Trace,$Item);$Trace.Add('callback');switch($Mode){'zero'{return};'one'{$Item;return};'many'{$Item;23;return};'null'{$null;return};'fail'{throw 'failure'};'partial'{$Item;throw 'failure'}}}
                    if($mode -eq 'invalid') {$callback=$null}
                    $caught=$null;$result=@()
                    try {$result=@(& $name -Callback $callback -Arguments @{Mode=$mode;Trace=$trace;Item=$item} -Trace $trace)}
                    catch {$caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;category=[string]$_.CategoryInfo.Category}}
                    $row=$result|Select-Object -First 1
                    [pscustomobject]@{name=$name;mode=$mode;count=$result.Count;type=if($row){$row.GetType().FullName}else{$null};before=$row.Before;after=$row.After;valueType=if($null -ne $row.Result){$row.Result.GetType().FullName}else{$null};values=@($row.Result);same=if($row){[object]::ReferenceEquals(@($row.Result)[0],$item)}else{$false};trace=@($trace);caught=$caught}|ConvertTo-Json -Depth 7 -Compress
                }
            }
            $trace=[Collections.Generic.List[string]]::new()
            [pscustomobject]@{value=Read-LiteralLocalClosure -Trace $trace;trace=@($trace)}|ConvertTo-Json -Depth 6 -Compress
            function Get-InvocationCommand {param($Item);$Item}
            $item=[pscustomobject]@{name='command'}
            $trace=[Collections.Generic.List[string]]::new()
            $commandValue=New-InvocationObject -Callback 'Get-InvocationCommand' -Arguments @{Item=$item} -Trace $trace
            [pscustomobject]@{commandSame=[object]::ReferenceEquals($commandValue.Result,$item);trace=@($trace)}|ConvertTo-Json -Compress
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"same\":true", generated);
        Assert.Contains("\"Owner\":\"owner\"", generated);
        Assert.Contains("\"commandSame\":true", generated);
    }

    [Theory]
    [InlineData("& $script:Callback")]
    [InlineData("& $Callback 2>$null")]
    public void LiteralLocalInvocations_UnqualifiedFormsRemainHosted(string expression)
    {
        using var fixture = ArtifactFixture.Create("function Read-Literal { [CmdletBinding()]param($Callback);[pscustomobject]@{Result="+expression+"} }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(new[] { fixture.ScriptPath },
            "Generated.LiteralInvocationBoundary", "CompiledPowerShell", "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Methods);
    }

    [Fact]
    public void LiteralLocalInvocations_RemainClosedWithoutNativeHostCapability()
    {
        var document = PowerShellSourceParser.Parse("function Read-Literal { param([scriptblock]$Callback);[pscustomobject]@{Result=& $Callback} }",
            Path.Combine(Path.GetTempPath(), "literal-local-invocation-strict.ps1"));
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(strict.Emitted.Methods);
    }
}
