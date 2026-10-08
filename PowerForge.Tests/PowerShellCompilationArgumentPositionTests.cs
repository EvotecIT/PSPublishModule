using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ArgumentPosition_PreservesLastConditionalStatementAndConversionFailure(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ConditionalArgumentResult {
                [CmdletBinding()]param($Value,[int]$Mode,$Trace)
                try {
                    [Convert]::ToBase64String($(
                        $Trace.Add('argument')
                        if($Mode -eq 0){ $Value }
                        elseif($Mode -eq 1){ @($Value,1) }
                        elseif($Mode -eq 2){ $Trace.Add('selected'); $Value; 1 }
                        elseif($Mode -eq 3){ }
                        elseif($Mode -eq 4){ $(if($Value){$Value}else{1}) }
                        elseif($Mode -eq 5){ Get-OfflineArgumentValue -Value $Value -Trace $Trace }
                        elseif($Mode -eq 6){ [int]::Parse('invalid') }
                        elseif($Mode -eq 7){ $selected=$Value; $selected }
                        else{ Get-OfflineArgumentValue -Value $(if($Value){$Value}else{1}) -Trace $Trace }
                    ))
                }catch{
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                }finally{$Trace.Add('finally')}
            }
            function Get-ConditionalParseResult {
                [CmdletBinding()]param($Value,[bool]$Select,$Trace)
                try {
                    [int]::Parse($(if($Select){$Value}else{'invalid'}))
                }catch{
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                }finally{$Trace.Add('finally')}
            }
            function Get-AuthoredTypeArgumentResult {
                [CmdletBinding()]param([bool]$Select,$Trace)
                $type=[int]
                try {
                    [Convert]::ToBase64String($(if($Select){@($type)}else{@(1,2)}))
                }catch{
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                }finally{$Trace.Add('finally')}
            }
            function Get-MultipleArgumentResult {
                [CmdletBinding()]param($Value,[int]$Count,$Trace)
                try {
                    [Convert]::ToBase64String($(if($Count){$Value}else{@(1,2)}),0,$Count)
                }catch{
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                }finally{$Trace.Add('finally')}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ArgumentPosition", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(4, built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries, entry => Assert.True(entry.EmittedClrMethod, entry.Name));
        const string probe = """
            $ErrorActionPreference='Stop'
            function global:Get-OfflineArgumentValue {param($Value,$Trace) $Trace.Add('command'); $Value}
            foreach($mode in 0..8){foreach($case in 'byte','string','type'){
                $value=if($case -eq 'byte'){[byte]2}elseif($case -eq 'string'){'invalid'}else{[string]}
                $trace=[Collections.Generic.List[string]]::new()
                $records=@(Get-ConditionalArgumentResult -Value $value -Mode $mode -Trace $trace)
                [pscustomobject]@{mode=$mode;case=$case;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 6 -Compress
            }}
            foreach($select in $false,$true){foreach($value in '12','invalid'){
                $trace=[Collections.Generic.List[string]]::new()
                $records=@(Get-ConditionalParseResult -Value $value -Select:$select -Trace $trace)
                [pscustomobject]@{select=$select;value=$value;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 6 -Compress
            }}
            foreach($select in $false,$true){
                $trace=[Collections.Generic.List[string]]::new()
                $records=@(Get-AuthoredTypeArgumentResult -Select:$select -Trace $trace)
                [pscustomobject]@{kind='authored-type';select=$select;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 6 -Compress
            }
            foreach($count in 0,1,3){foreach($case in 'byte','string'){
                $trace=[Collections.Generic.List[string]]::new()
                $value=if($case -eq 'byte'){[byte[]]@(1,2)}else{'invalid'}
                $records=@(Get-MultipleArgumentResult -Value $value -Count $count -Trace $trace)
                [pscustomobject]@{kind='multiple';case=$case;count=$count;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 6 -Compress
            }}
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        Assert.Equal(original, RunModuleProof(built.ArtifactPath!, probe, host));
        Assert.Equal(39, original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("finally", original);
        Assert.Contains("MethodInvocationException", original);
    }
}
