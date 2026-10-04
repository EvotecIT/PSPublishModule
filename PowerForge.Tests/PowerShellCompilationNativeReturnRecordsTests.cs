namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeReturnPipelines_PreserveCollectionRecordsAndParenthesizedScalarization(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-Record { [CmdletBinding()]param([string]$Mode) process {
                $list=[System.Collections.Generic.List[object]]::new()
                $list.Add('one');$list.Add('two')
                switch($Mode) { 'empty' {return}; 'null' {$null;return}; 'many' {,$list;,$list;return}; 'throw' {,$list;throw 'failure'} }
                return ,$list
            } }
            function Read-Direct { [CmdletBinding()]param([string]$Mode='one') process {return Get-Record $Mode} }
            function Read-Wrapped { [CmdletBinding()]param([string]$Mode='one') process {return (Get-Record $Mode)} }
            function Read-Captured { [CmdletBinding()]param([string]$Mode='one') process {$value=@(try {'before';return Get-Record $Mode} finally {'finally'});'unreachable'} }
            Export-ModuleMember -Function Read-Direct,Read-Wrapped,Read-Captured
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeReturnRecords", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(4, built.Manifest!.CompiledMethods);
        const string probe = """
            foreach($name in 'Read-Direct','Read-Wrapped','Read-Captured') {
                foreach($mode in 'one','empty','null','many','throw') {
                    $rows=@();$caught=$null
                    try { & $name $mode|ForEach-Object { $rows+=,[pscustomobject]@{type=if($null -eq $_){'<null>'}else{$_.GetType().FullName};values=@($_|ForEach-Object {[string]$_})} } }
                    catch {$caught=$_.FullyQualifiedErrorId+'|'+$_.Exception.GetType().FullName}
                    [pscustomobject]@{name=$name;mode=$mode;rows=$rows;caught=$caught}|ConvertTo-Json -Depth 7 -Compress
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original:" + original + Environment.NewLine + "Generated:" + generated);
        Assert.Contains("List`1", generated);
    }
}
