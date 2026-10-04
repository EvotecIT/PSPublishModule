using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void SdkMetadata_PreservesMemberInspectionAndCommandIdentity(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSScriptTools", "MemberInspection", "Show-HiddenMember.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function Read-CommandMetadata {
                [CmdletBinding()]param([System.Management.Automation.CommandInfo]$Command)
                [pscustomobject]@{Name=$Command.Name;Kind=$Command.CommandType.ToString();Module=$Command.ModuleName;Same=$Command}
            }
            function Read-MetadataContainers {
                [CmdletBinding()]param([System.Management.Automation.CommandInfo[]]$Commands,[Collections.Generic.List[System.Management.Automation.PSMemberTypes]]$Kinds)
                [pscustomobject]@{commands=$Commands;kinds=$Kinds}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.SdkMetadata", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.True(built.Manifest!.CompiledMethods == 3, string.Join(Environment.NewLine,
            built.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            function global:OfflineMetadataCommand { [CmdletBinding()]param([string]$Name) 'never execute' }
            Set-Alias -Name OfflineMetadataAlias -Value OfflineMetadataCommand -Scope Global
            foreach($sample in 'string','date','notes') {
                $value=switch($sample){'string'{'text'};'date'{[datetime]::new(2020,1,2)};default{[pscustomobject]@{Name='note';Value=7}}}
                foreach($kind in 'All','Method','Properties') {
                    foreach($exclude in $false,$true) {
                        foreach($pipeline in $false,$true) {
                            $info=$null
                            $failures=$null
                            $rows=if($pipeline){@($value,$value)|Show-HiddenMember -MemberType $kind -ExcludePropertyMethod:$exclude -InformationVariable info -ErrorAction SilentlyContinue -ErrorVariable failures}else{Show-HiddenMember -InputObject $value -MemberType $kind -ExcludePropertyMethod:$exclude -InformationVariable info -ErrorAction SilentlyContinue -ErrorVariable failures}
                            $projection=@($rows|ForEach-Object {[pscustomobject]@{name=$_.Name;kind=$_.MemberType.ToString();type=$_.Type;custom=$_.PSObject.TypeNames[0]}})
                            $information=@($info|ForEach-Object {[pscustomobject]@{tags=@($_.Tags);names=@($_.MessageData|ForEach-Object Name)}})
                            $errors=@($failures|ForEach-Object {[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}})
                            [pscustomobject]@{sample=$sample;kind=$kind;exclude=$exclude;pipeline=$pipeline;rows=$projection;info=$information;errors=$errors}|ConvertTo-Json -Depth 8 -Compress
                        }
                    }
                }
            }
            foreach($name in 'Get-Date','OfflineMetadataCommand','OfflineMetadataAlias') {
                $command=Get-Command $name -ErrorAction Stop
                $result=Read-CommandMetadata -Command $command
                [pscustomobject]@{name=$result.Name;kind=$result.Kind;module=$result.Module;same=[object]::ReferenceEquals($command,$result.Same)}|ConvertTo-Json -Compress
            }
            $commands=[Management.Automation.CommandInfo[]]@((Get-Command Get-Date),(Get-Command OfflineMetadataAlias))
            $kinds=[Collections.Generic.List[Management.Automation.PSMemberTypes]]::new()
            $kinds.Add([Management.Automation.PSMemberTypes]::Method)
            $kinds.Add([Management.Automation.PSMemberTypes]::Property)
            $result=Read-MetadataContainers -Commands $commands -Kinds $kinds
            [pscustomobject]@{commands=@($result.commands.Name);sameCommand=[object]::ReferenceEquals($commands[0],$result.commands[0]);sameKinds=[object]::ReferenceEquals($kinds,$result.kinds);kinds=@($result.kinds|ForEach-Object ToString)}|ConvertTo-Json -Compress
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Equal(40, generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("psHiddenMember", generated);
        Assert.Contains("\"same\":true", generated);
        Assert.Contains("\"sameKinds\":true", generated);
    }
}
