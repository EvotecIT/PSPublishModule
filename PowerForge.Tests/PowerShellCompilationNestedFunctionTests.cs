namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NestedFunctions_PreserveDeclarationScopeMetadataRecursionAndEscape(string framework, string host)
    {
        using var fixture=ArtifactFixture.Create("""
            function Invoke-Nested {
                [CmdletBinding()] param([string]$Outer,[switch]$Declare,[switch]$Protect)
                if($Protect) { Set-Item function:Read-Nested -Value { 'protected' } -Options ReadOnly }
                if($Declare) {
                    function Read-Nested {
                        [CmdletBinding()] [Alias('Read-NestedAlias')] param([string]$Suffix='default')
                        if($Suffix -eq 'failure') { throw 'nested failure' }
                        $Outer+':'+$Suffix
                    }
                }
                Read-Nested -Suffix 'first'
                $Outer='changed'
                Read-NestedAlias -Suffix 'second'
                (Get-Command Read-Nested).ScriptBlock
            }
            function Invoke-NestedRecursive {
                [CmdletBinding()] param([int]$Value)
                function Read-Sum { param([int]$N) if($N -le 0) { return 0 }; return $N+(Read-Sum -N ($N-1)) }
                Read-Sum -N $Value
            }
            function Invoke-NestedFailure {
                [CmdletBinding()] param([string]$Value)
                function Read-Failing { param([string]$Text) try { if($Text -eq 'bad') { throw 'nested failure' }; $Text } finally { 'finally' } }
                try { Read-Failing -Text $Value } catch { $_.FullyQualifiedErrorId; $_.Exception.Message }
                'after'
            }
            function Invoke-NestedLifecycle {
                [CmdletBinding()] param([int[]]$Values)
                function local:Read-Child {
                    [CmdletBinding()] param([Parameter(ValueFromPipeline)][int]$Value)
                    begin { 'begin' }
                    process { 'process:'+ $Value }
                    end { 'end' }
                }
                $Values | Read-Child
            }
            """, ".psm1");
        var built=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.NestedFunctions",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true) { TargetFramework=framework });
        Assert.True(built.Succeeded,built.Error+Environment.NewLine+built.BuildOutput);
        foreach(var name in new[] { "Invoke-Nested", "Invoke-NestedRecursive", "Invoke-NestedFailure", "Invoke-NestedLifecycle" })
        {
            var unit=Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,unit=>unit.Name==name);
            Assert.True(unit.EmittedClrMethod,System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.NotEmpty(unit.RegionGraph!.ScriptBlocks);
        }
        const string probe="""
            & (Get-Command Invoke-Nested).Module {
                function Read-Nested { param($Suffix) 'outer:'+ $Suffix }
                function Read-NestedAlias { param($Suffix) 'outer-alias:'+ $Suffix }
                foreach($declare in $false,$true,$false,$true) {
                    $records=@(Invoke-Nested -Outer 'original' -Declare:$declare)
                    [pscustomobject]@{declare=$declare;records=@($records | Where-Object { $_ -isnot [scriptblock] });
                        outside=(Read-Nested -Suffix 'outside');aliasOutside=(Read-NestedAlias -Suffix 'outside')} | ConvertTo-Json -Compress
                    $escaped=$records[-1]
                    $Outer='caller'
                    [pscustomobject]@{escaped=@(& $escaped -Suffix 'escaped');parameters=@($escaped.Ast.ParamBlock.Parameters | ForEach-Object {$_.Name.VariablePath.UserPath})} | ConvertTo-Json -Compress
                }
                foreach($n in 0,1,5,2) { [pscustomobject]@{n=$n;sum=@(Invoke-NestedRecursive -Value $n)} | ConvertTo-Json -Compress }
                foreach($value in 'ok','bad','ok') { [pscustomobject]@{value=$value;records=@(Invoke-NestedFailure -Value $value)} | ConvertTo-Json -Compress }
                foreach($values in @(@(),@(1),@(1,2,3))) { [pscustomobject]@{values=$values;records=@(Invoke-NestedLifecycle -Values $values)} | ConvertTo-Json -Compress }
                try { Invoke-Nested -Outer 'original' -Declare -Protect -ErrorAction Stop }
                catch { [pscustomobject]@{protectedError=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName} | ConvertTo-Json -Compress }
            }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        Assert.True(original==generated,"Original: "+original+Environment.NewLine+"Generated: "+generated);
        Assert.Contains("nested failure",generated);
        Assert.Contains("protectedError",generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NestedFunctions_QualifyUnchangedOfflineCloudReportMerge(string framework,string host)
    {
        var source=FindCompleteConversionWorkflow("CleanupMonster","FullModule","Private","Merge-CloudDeviceReportInventory.ps1");
        using var fixture=ArtifactFixture.Create(File.ReadAllText(source),".psm1");
        var built=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.CloudReportMerge",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true) { TargetFramework=framework });
        Assert.True(built.Succeeded,built.Error+Environment.NewLine+built.BuildOutput);
        var unit=Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,unit=>unit.Name=="Merge-CloudDeviceReportInventory");
        Assert.True(unit.EmittedClrMethod,System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.Single(unit.RegionGraph!.ScriptBlocks);
        const string probe="""
            $primary=@([pscustomobject]@{Name='primary';ManagedDeviceId='INTUNE-1';DeviceId='device-1'})
            $additional=@(
                [pscustomobject]@{Name='duplicate';ManagedDeviceId='intune-1'},
                [pscustomobject]@{Name='new';EntraDeviceObjectId='entra-2';AutopilotDeviceId='auto-2'},
                [pscustomobject]@{Name='duplicate-new';AutopilotDeviceId='AUTO-2'},
                [pscustomobject]@{Name='keyless'},
                [pscustomobject]@{Name='keyless-two'})
            foreach($case in @(
                [ordered]@{PrimaryDevices=@();AdditionalDevices=@()},
                [ordered]@{PrimaryDevices=$primary;AdditionalDevices=@()},
                [ordered]@{PrimaryDevices=@();AdditionalDevices=$additional},
                [ordered]@{PrimaryDevices=$primary;AdditionalDevices=$additional},
                [ordered]@{PrimaryDevices=$primary;AdditionalDevices=$additional})) {
                $records=@(Merge-CloudDeviceReportInventory @case)
                [pscustomobject]@{records=$records;primaryUnchanged=$primary;additionalUnchanged=$additional;
                    helperVisible=[bool](Get-Command Get-CloudDeviceReportInventoryKey -ErrorAction SilentlyContinue)} | ConvertTo-Json -Depth 6 -Compress
            }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        Assert.True(original==generated,"Original: "+original+Environment.NewLine+"Generated: "+generated);
        Assert.Contains("\"helperVisible\":false",generated);
    }
}
