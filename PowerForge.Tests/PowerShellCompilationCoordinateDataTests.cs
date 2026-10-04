using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void CoordinateData_PreservesPinnedProgressRecordsAndCursorErrors(string framework,string host)
    {
        var source=FindCompleteConversionWorkflow("PSScriptTools","Coordinates","Write-AnsiProgress.ps1");
        var observer=FindCompleteConversionWorkflow("PSScriptTools","Coordinates","Observe.ps1");
        Assert.Equal("2768F1FDA6B2ECF58B0227B5A29B2F96C5C9ED2C93C58AFEC7DDE764BFC7A493",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture=ArtifactFixture.Create(File.ReadAllText(source),".psm1");
        var result=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.CoordinateProgress",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true){TargetFramework=framework});
        Assert.True(result.Succeeded,result.Error+Environment.NewLine+result.BuildOutput);
        Assert.True(Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries).EmittedClrMethod);
        string Observe(string module)=>RunModuleProof(module,
            $"& '{observer.Replace("'","''",StringComparison.Ordinal)}' -ModulePath '{module.Replace("'","''",StringComparison.Ordinal)}'",host);
        var original=Observe(fixture.ScriptPath);
        Assert.Equal(original,Observe(result.ArtifactPath!));
        Assert.Equal(11,original.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("9632",original);
        Assert.Contains("9608",original);
        Assert.Contains("9679",original);
        Assert.Contains("SetCursorPosition",original);
    }

    private const string OfflineCoordinateFunctions = """
        function Move-OfflineCoordinates {
            [CmdletBinding()]param([System.Management.Automation.Host.Coordinates]$Position)
            $Position.X=$Position.X+1
            $Position.Y+=2
            $Position
        }
        function New-OfflineCoordinates {
            [CmdletBinding()]param([int]$X,[int]$Y)
            [System.Management.Automation.Host.Coordinates]::new($X,$Y)
        }
        function Read-OfflineCoordinates {
            [CmdletBinding()]param([Parameter(ValueFromPipeline)][System.Management.Automation.Host.Coordinates[]]$Position)
            process {foreach($point in $Position){[pscustomobject]@{x=$point.X;y=$point.Y;type=$point.GetType().FullName}}}
        }
        function Move-OfflineCoordinateArray {
            [CmdletBinding()]param([System.Management.Automation.Host.Coordinates[]]$Position)
            $Position[0].X+=5
            $Position
        }
        function Move-OfflineCoordinateList {
            [CmdletBinding()]param([System.Collections.Generic.List[System.Management.Automation.Host.Coordinates]]$Position)
            $Position[0].X+=5
            $Position
        }
        function Read-OfflineNullableCoordinate {
            [CmdletBinding()]param([System.Nullable[System.Management.Automation.Host.Coordinates]]$Position)
            if($null -eq $Position){'empty'}else{$Position.X}
        }
        """;

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void CoordinateData_RequiresNativeHostBinding(string framework)
    {
        var source=PowerShellSourceParser.Parse(OfflineCoordinateFunctions,Path.Combine(Path.GetTempPath(),"offline-coordinate.psm1"));
        foreach(var capabilities in new[]{PowerShellCompilationCapabilities.TypedLibrary,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes})
            Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,capabilities).Emitted.Methods);
    }

    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void CoordinateData_PreservesConstructionBindingAndValueCopies(string framework,string host)
    {
        using var fixture=ArtifactFixture.Create(OfflineCoordinateFunctions,".psm1");
        var result=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.CoordinateData",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true){TargetFramework=framework});
        Assert.True(result.Succeeded,result.Error+Environment.NewLine+result.BuildOutput);
        Assert.Equal(6,result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries,entry=>Assert.True(entry.UsesNativeFunctionBinding));
        const string probe="""
            $ErrorActionPreference='Stop'
            foreach($x in -1,0,3){foreach($y in 0,4){
                $point=New-OfflineCoordinates -X $x -Y $y
                $changed=Move-OfflineCoordinates -Position $point
                [pscustomobject]@{case='copy';original=@($point.X,$point.Y);changed=@($changed.X,$changed.Y);type=$changed.GetType().FullName}|ConvertTo-Json -Compress
            }}
            $points=@([Management.Automation.Host.Coordinates]::new(1,2),[Management.Automation.Host.Coordinates]::new(3,4))
            foreach($pipeline in $false,$true){
                $records=if($pipeline){@($points|Read-OfflineCoordinates)}else{@(Read-OfflineCoordinates -Position $points)}
                [pscustomobject]@{case='array';pipeline=$pipeline;records=$records}|ConvertTo-Json -Compress -Depth 5
            }
            $changed=@(Move-OfflineCoordinateArray -Position $points)
            [pscustomobject]@{case='array-mutation';original=@($points|ForEach-Object {$_.X});changed=@($changed|ForEach-Object {$_.X})}|ConvertTo-Json -Compress
            [Management.Automation.Host.Coordinates[]]$typedPoints=@([Management.Automation.Host.Coordinates]::new(1,2),[Management.Automation.Host.Coordinates]::new(3,4))
            $changed=@(Move-OfflineCoordinateArray -Position $typedPoints)
            [pscustomobject]@{case='typed-array-mutation';original=@($typedPoints|ForEach-Object {$_.X});changed=@($changed|ForEach-Object {$_.X});type=$typedPoints.GetType().FullName}|ConvertTo-Json -Compress
            $list=[Collections.Generic.List[Management.Automation.Host.Coordinates]]::new()
            $list.Add([Management.Automation.Host.Coordinates]::new(1,2))
            $changed=@(Move-OfflineCoordinateList -Position $list)
            [pscustomobject]@{case='list-mutation';original=@($list|ForEach-Object {$_.X});changed=@($changed|ForEach-Object {$_.X});type=$list.GetType().FullName}|ConvertTo-Json -Compress
            foreach($point in $null,[Management.Automation.Host.Coordinates]::new(3,4)){
                [pscustomobject]@{case='nullable';value=(Read-OfflineNullableCoordinate -Position $point)}|ConvertTo-Json -Compress
            }
            foreach($bad in 'invalid',42,[pscustomobject]@{X='bad';Y=2}){
                try {Move-OfflineCoordinates -Position $bad|Out-Null}
                catch {[pscustomobject]@{case='binding';id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName}|ConvertTo-Json -Compress}
            }
            [pscustomobject]@{case='metadata';type=(Get-Command Move-OfflineCoordinates).Parameters['Position'].ParameterType.FullName}|ConvertTo-Json -Compress
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        Assert.Equal(original,RunModuleProof(result.ArtifactPath!,probe,host));
        Assert.Equal(17,original.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"original\":[3,4],\"changed\":[4,6]",original);
        Assert.Contains("ParameterBinding",original);
    }
}
