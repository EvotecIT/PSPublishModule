using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void StaticEnumAssignment_PreservesNativeFlagsConversionFailureAndFinally(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Set-OfflineProtocol {
                [CmdletBinding()]param($Value,$Trace,[string]$Mode,[switch]$Fail)
                $old=[Net.ServicePointManager]::SecurityProtocol
                try {
                    switch($Mode){
                        'plain'{[Net.ServicePointManager]::SecurityProtocol=$Value}
                        'combine'{[Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor $Value}
                        'add'{[Net.ServicePointManager]::SecurityProtocol+=$Value}
                        'subtract'{[Net.ServicePointManager]::SecurityProtocol-=$Value}
                        'multiply'{[Net.ServicePointManager]::SecurityProtocol*=$Value}
                        'divide'{[Net.ServicePointManager]::SecurityProtocol/=$Value}
                        'remainder'{[Net.ServicePointManager]::SecurityProtocol%=$Value}
                    }
                    $Trace.Add('assigned')
                    if($Fail){throw [InvalidOperationException]::new('after-assignment')}
                    [int][Net.ServicePointManager]::SecurityProtocol
                }catch{[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;protocol=[int][Net.ServicePointManager]::SecurityProtocol}}
                finally{$Trace.Add('finally');[Net.ServicePointManager]::SecurityProtocol=$old}
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StaticEnum", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.True(Assert.Single(result.Manifest.UnitDispositionLedger!.Entries).UsesNativeFunctionBinding);
        const string probe = """
            foreach($mode in 'plain','combine','add','subtract','multiply','divide','remainder'){foreach($fail in $false,$true){foreach($value in [Net.SecurityProtocolType]::Tls12,3072,'Tls12','Tls12, Tls11','invalid',1,$null){
                $trace=[Collections.Generic.List[string]]::new();$before=[Net.ServicePointManager]::SecurityProtocol
                $records=@(Set-OfflineProtocol -Value $value -Trace $trace -Mode $mode -Fail:$fail)
                [pscustomobject]@{mode=$mode;fail=$fail;input=$value;records=$records;trace=@($trace.ToArray());restored=([Net.ServicePointManager]::SecurityProtocol -eq $before)}|ConvertTo-Json -Depth 8 -Compress
            }}}
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        Assert.Equal(original,RunModuleProof(result.ArtifactPath!,probe,host));
        Assert.Equal(98,original.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"restored\":true",original);
        Assert.Contains("after-assignment",original);
        Assert.Contains("SetValueInvocationException",original);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void StaticEnumAssignment_PreservesStrictClrAndUnavailableStorageBoundaries(string framework)
    {
        var source=PowerShellSourceParser.Parse("""
            function Set-TypedProtocol {param([Net.SecurityProtocolType]$Value);[Net.ServicePointManager]::SecurityProtocol=$Value}
            function Set-ObjectProtocol {param($Value);[Net.ServicePointManager]::SecurityProtocol=$Value}
            function Set-Constant {param();[Net.SecurityProtocolType]::Tls12=3072}
            function Set-Unavailable {param();[Offline.MissingStorage]::Value=3072}
            """,Path.Combine(Path.GetTempPath(),"static-enum-boundaries.ps1"));
        var strict=new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,PowerShellCompilationCapabilities.TypedLibrary);
        var typed=Assert.Single(strict.Emitted.Methods);
        Assert.Null(typed.NativeFunctionBinding);
        var hybrid=new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(2,hybrid.Emitted.Methods.Length);
        Assert.All(hybrid.Emitted.Methods,method=>Assert.NotNull(method.NativeFunctionBinding));
    }
}
