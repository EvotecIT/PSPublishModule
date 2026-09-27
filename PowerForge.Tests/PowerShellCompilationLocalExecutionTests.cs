using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void LocalExecution_PreservesBorrowedIdentityPoolResultsAndCallerCleanup(string framework,string host)
    {
        var names=new[]{"New-Runspace","Start-Runspace","Stop-Runspace"};
        var paths=names.Select(name=>FindCompleteConversionWorkflow("PSSharedGoods","FullModule","Public","Runspaces",name+".ps1")).ToArray();
        Assert.Equal(new[]{"B79F20CADB05A05EB0AD67D76C92B6C12BF141C7E9940E2FEA244A3CA3CF651B","972800110C64BB1D9DA09603953B5E1F9272F34D92D557A0E2BB6BB3B7E58E4F","D4F7D15145F5E7034CFA8788FA1B8C9DE091A38C6FD3261F1C1EFD21AE32518A"},
            paths.Select(path=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))).ToArray());
        using var fixture=ArtifactFixture.Create(string.Join(Environment.NewLine,paths.Select(File.ReadAllText))+"""

            function Invoke-BorrowedPipeline {
                [CmdletBinding()]param([PowerShell]$Pipeline,$Trace)
                try {
                    $records=$Pipeline.Invoke()
                    [pscustomobject]@{same=[object]::ReferenceEquals($Pipeline,$PSBoundParameters.Pipeline);records=@($records);state=[string]$Pipeline.InvocationStateInfo.State;errors=@($Pipeline.Streams.Error|ForEach-Object {$_.FullyQualifiedErrorId})}
                }catch{[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                finally{$Trace.Add('finally')}
            }
            function Get-BorrowedPool {
                [CmdletBinding()]param([System.Management.Automation.Runspaces.RunspacePool]$Pool)
                [pscustomobject]@{same=[object]::ReferenceEquals($Pool,$PSBoundParameters.Pool);state=[string]$Pool.RunspacePoolStateInfo.State;minimum=$Pool.GetMinRunspaces();maximum=$Pool.GetMaxRunspaces()}
            }
            """, ".psm1");
        var result=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.LocalExecution",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true){TargetFramework=framework});
        Assert.True(result.Succeeded,result.Error+Environment.NewLine+result.BuildOutput);
        Assert.Equal(5,result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries,entry=>{
            Assert.True(entry.EmittedClrMethod);Assert.True(entry.UsesNativeFunctionBinding);Assert.False(entry.RetainedHostedSource);
        });
        const string probe="""
            function Get-OfflineRecordShape($Value){
                if($null -eq $Value){return $null}
                if($Value -is [Management.Automation.ErrorRecord]){return [pscustomobject]@{type=$Value.GetType().FullName;id=$Value.FullyQualifiedErrorId;exception=$Value.Exception.GetType().FullName;message=$Value.Exception.Message;line=$Value.InvocationInfo.ScriptLineNumber;column=$Value.InvocationInfo.OffsetInLine;targetType=if($Value.TargetObject){$Value.TargetObject.GetType().FullName}}}
                if($Value -is [Collections.IDictionary]){return [pscustomobject]@{type=$Value.GetType().FullName;keys=@($Value.Keys|Sort-Object);output=@($Value.Output|ForEach-Object {Get-OfflineRecordShape $_});errors=@($Value.Errors|ForEach-Object {Get-OfflineRecordShape $_})}}
                if($Value -is [Array]){return [pscustomobject]@{type=$Value.GetType().FullName;count=$Value.Count;items=@($Value|ForEach-Object {Get-OfflineRecordShape $_})}}
                [pscustomobject]@{type=$Value.GetType().FullName;value=$Value}
            }
            foreach($case in 'success','stream-error','terminating','disposed','null'){
                $pipeline=$null;$trace=[Collections.Generic.List[string]]::new()
                try {
                    if($case -ne 'null'){$pipeline=[PowerShell]::Create();[void]$pipeline.AddScript($(switch($case){'stream-error'{"'before';Write-Error 'offline-error';'after'"}'terminating'{"'before';throw 'offline-throw'"}default{"'one';2"}}))}
                    if($case -eq 'disposed'){$pipeline.Dispose()}
                    $records=@(Invoke-BorrowedPipeline -Pipeline $pipeline -Trace $trace)
                    $reuse=@()
                    if($case -in 'success','stream-error','terminating'){$pipeline.Commands.Clear();[void]$pipeline.AddScript("'reused'");$reuse=@($pipeline.Invoke())}
                    [pscustomobject]@{kind='borrowed';case=$case;records=$records;trace=@($trace.ToArray());reuse=$reuse;callerState=if($pipeline){[string]$pipeline.InvocationStateInfo.State}}|ConvertTo-Json -Depth 8 -Compress
                }finally{if($pipeline){$pipeline.Dispose()}}
            }
            foreach($extended in $false,$true){foreach($case in 'success','warning','stream-error','terminating','empty'){
                $pool=$null;$jobs=@();$records=@();$warnings=@();$errors=@();$outer=$null
                try {
                    $pool=New-Runspace -minRunspaces 1 -maxRunspaces 1
                    $before=Get-BorrowedPool -Pool $pool
                    if($case -ne 'empty'){
                        $script=switch($case){'warning'{{param($Value);Write-Warning 'offline-warning';$Value}}'stream-error'{{param($Value);Write-Error 'offline-error';$Value}}'terminating'{{param($Value);$Value;throw 'offline-throw'}}default{{param($Value);$Value;$Value+1}}}
                        $job=Start-Runspace -ScriptBlock $script -Parameters @{Value=7} -RunspacePool $pool
                        $jobs=@($job)
                        if(-not $job.Status.AsyncWaitHandle.WaitOne(5000)){throw 'Offline child exceeded bounded wait'}
                    }
                    try {$records=@(Stop-Runspace -Runspaces $jobs -FunctionName 'offline' -RunspacePool $pool -ExtendedOutput:$extended -WarningVariable warnings -ErrorVariable errors -ErrorAction Continue 2>$null 3>$null)}catch{$outer=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                    [pscustomobject]@{kind='pool';case=$case;extended=$extended;before=$before;records=@($records|ForEach-Object {Get-OfflineRecordShape $_});state=[string]$pool.RunspacePoolStateInfo.State;pending=@($jobs|Where-Object {$null -ne $_.Status}).Count;warnings=@($warnings|ForEach-Object {[string]$_});errors=@($errors|ForEach-Object {$_.FullyQualifiedErrorId});outer=$outer}|ConvertTo-Json -Depth 12 -Compress
                }finally{
                    foreach($job in $jobs){$job.Pipe.Stop();$job.Pipe.Dispose()}
                    if($pool){$pool.Close();$pool.Dispose()}
                }
            }}
            foreach($range in @(@(0,1),@(2,1))){try{New-Runspace -minRunspaces $range[0] -maxRunspaces $range[1] -ErrorAction Stop}catch{[pscustomobject]@{kind='invalid-pool';minimum=$range[0];maximum=$range[1];id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName}|ConvertTo-Json -Compress}}
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        Assert.Equal(original,RunModuleProof(result.ArtifactPath!,probe,host));
        Assert.Equal(17,original.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries).Count(line=>line.StartsWith('{')));
        Assert.Contains("\"same\":true",original);
        Assert.Contains("\"state\":\"Closed\"",original);
        Assert.Contains("offline-throw",original);
        Assert.Contains("\"reuse\":[\"reused\"]",original);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void LocalExecution_RequiresNativeHostBindingAndKeepsOtherSdkTypesClosed(string framework)
    {
        var source=PowerShellSourceParser.Parse("""
            function Read-Pipeline {param([PowerShell]$Value);$Value}
            function Read-Pool {param([System.Management.Automation.Runspaces.RunspacePool]$Value);$Value}
            function Read-PipelineArray {param([PowerShell[]]$Value);$Value}
            function Read-PoolList {param([Collections.Generic.List[System.Management.Automation.Runspaces.RunspacePool]]$Value);$Value}
            function New-Pipeline {param();[PowerShell]::Create()}
            function Read-Runspace {param([System.Management.Automation.Runspaces.Runspace]$Value);$Value}
            function Read-RunspaceArray {param([System.Management.Automation.Runspaces.Runspace[]]$Value);$Value}
            function Read-RunspaceList {param([Collections.Generic.List[System.Management.Automation.Runspaces.Runspace]]$Value);$Value}
            function Read-Coordinates {param([System.Management.Automation.Host.Coordinates]$Value);$Value}
            """,Path.Combine(Path.GetTempPath(),"local-execution-boundaries.ps1"));
        var hybrid=new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(8,hybrid.Emitted.Methods.Length);
        foreach(var capabilities in new[]{PowerShellCompilationCapabilities.TypedLibrary,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes})
            Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,capabilities).Emitted.Methods);
    }
}
