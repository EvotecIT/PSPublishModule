using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void PSSessionData_PreservesOfflinePinnedFunctionCopy(string framework,string host)
    {
        var source=FindCompleteConversionWorkflow("PSScriptTools","SessionCopy","Copy-PSFunction.ps1");
        var observer=FindCompleteConversionWorkflow("PSScriptTools","SessionCopy","Observe.ps1");
        Assert.Equal("3B527D3E31221935E8C7C7F40AD3FFD0EB12EB194E02F4AEED8D65B9ECDBBDDA",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture=ArtifactFixture.Create(File.ReadAllText(source),".psm1");
        var result=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.SessionCopy",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true){TargetFramework=framework});
        Assert.True(result.Succeeded,result.Error+Environment.NewLine+result.BuildOutput);
        Assert.True(Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries).EmittedClrMethod);
        string Observe(string module)=>RunModuleProof(module,
            $"& '{observer.Replace("'","''",StringComparison.Ordinal)}' -ModulePath '{module.Replace("'","''",StringComparison.Ordinal)}'",host);
        var original=Observe(fixture.ScriptPath);
        Assert.Equal(original,Observe(result.ArtifactPath!));
        Assert.Equal(18,original.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("offline copy failure",original);
        Assert.Contains("BeforeOpen",original);
    }

    private const string OfflinePSSessionFunctions = """
        function Set-OfflineSessionName {
            [CmdletBinding()]param([Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session,[string]$Name,$Trace)
            try {
                [void]$Trace.Add('entered')
                $Session.Name=$Name
                $Session
                'second'
            } finally {[void]$Trace.Add('finally')}
        }
        function Read-OfflineSessions {
            [CmdletBinding()]param([Parameter(ValueFromPipeline)][System.Management.Automation.Runspaces.PSSession[]]$Session)
            process {foreach($item in $Session){if($null -eq $item){'null'}else{$item}}}
        }
        function Read-OfflineSessionMap {
            [CmdletBinding()]param([Collections.Generic.Dictionary[string,System.Management.Automation.Runspaces.PSSession]]$Sessions)
            foreach($key in $Sessions.Keys){$Sessions[$key]}
        }
        """;

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void PSSessionData_RequiresBothNativeBindingAndHostTypes(string framework)
    {
        var source=PowerShellSourceParser.Parse(OfflinePSSessionFunctions, Path.Combine(Path.GetTempPath(),"offline-session.psm1"));
        foreach(var capabilities in new[]
        {
            PowerShellCompilationCapabilities.TypedLibrary,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes
        })
        {
            var result=new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,capabilities);
            Assert.Empty(result.Emitted.Methods);
        }
    }

    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void PSSessionData_PreservesClosedSessionIdentityBindingAndCallerOwnership(string framework,string host)
    {
        using var fixture=ArtifactFixture.Create(OfflinePSSessionFunctions,".psm1");
        var result=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.SessionData",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true){TargetFramework=framework});
        Assert.True(result.Succeeded,result.Error+Environment.NewLine+result.BuildOutput);
        Assert.Equal(3,result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries,entry=>Assert.True(entry.UsesNativeFunctionBinding));
        const string probe="""
            $ErrorActionPreference='Stop'
            # Construction only: neither remote runspace is opened or connected.
            $runspaces=@();$sessions=@()
            try {
                foreach($name in 'first','second') {
                    $connection=[System.Management.Automation.Runspaces.WSManConnectionInfo]::new()
                    $runspace=[System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace($connection)
                    $runspaces+=,$runspace
                    if($runspace.RunspaceStateInfo.State -ne 'BeforeOpen'){throw 'Unexpected opened runspace'}
                    $constructor=[System.Management.Automation.Runspaces.PSSession].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') |
                        Where-Object {$_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.IsInstanceOfType($runspace)}
                    $session=$constructor.Invoke([object[]]@($runspace));$session.Name=$name;$sessions+=,$session
                }
                foreach($stop in $false,$true){
                    $trace=[Collections.Generic.List[string]]::new();$sessions[0].Name='prior'
                    $records=if($stop){@(Set-OfflineSessionName -Session $sessions[0] -Name 'changed' -Trace $trace | Select-Object -First 1)}
                             else{@(Set-OfflineSessionName -Session $sessions[0] -Name 'changed' -Trace $trace)}
                    [pscustomobject]@{case='mutation';stop=$stop;name=$sessions[0].Name;identity=[object]::ReferenceEquals($records[0],$sessions[0]);count=$records.Count;trace=@($trace);state=$runspaces[0].RunspaceStateInfo.State.ToString()}|ConvertTo-Json -Compress
                }
                foreach($kind in 'null','one','many','pipeline','map'){
                    $records=switch($kind){
                        null {@(Read-OfflineSessions -Session $null)}
                        one {@(Read-OfflineSessions -Session $sessions[0])}
                        many {@(Read-OfflineSessions -Session @($sessions[0],$null,$sessions[1]))}
                        pipeline {@($sessions | Read-OfflineSessions)}
                        map {$map=[Collections.Generic.Dictionary[string,System.Management.Automation.Runspaces.PSSession]]::new();$map.Add('one',$sessions[0]);@(Read-OfflineSessionMap -Sessions $map)}
                    }
                    [pscustomobject]@{case=$kind;count=@($records).Count;names=@($records|ForEach-Object {if($_ -is [string]){$_}else{$_.Name}});identities=@($records|ForEach-Object {if($_ -is [string]){$true}else{[object]::ReferenceEquals($_,$sessions[0]) -or [object]::ReferenceEquals($_,$sessions[1])}})}|ConvertTo-Json -Compress
                }
                foreach($bad in 42,'not-a-session',[pscustomobject]@{Name='fake'}){
                    $trace=[Collections.Generic.List[string]]::new()
                    try {Set-OfflineSessionName -Session $bad -Name 'bad' -Trace $trace|Out-Null}
                    catch {[pscustomobject]@{case='binding';error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;trace=@($trace)}|ConvertTo-Json -Compress}
                }
                $parameter=(Get-Command Set-OfflineSessionName).Parameters['Session']
                [pscustomobject]@{case='metadata';type=$parameter.ParameterType.FullName;mandatory=@($parameter.Attributes|Where-Object {$_ -is [Management.Automation.ParameterAttribute]})[0].Mandatory;states=@($runspaces|ForEach-Object {$_.RunspaceStateInfo.State.ToString()})}|ConvertTo-Json -Compress
                if(@($runspaces|Where-Object {$_.RunspaceStateInfo.State -ne 'BeforeOpen'}).Count){throw 'Session consumer opened a runspace'}
            }finally {foreach($runspace in $runspaces){$runspace.Dispose()}}
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        Assert.Equal(original,RunModuleProof(result.ArtifactPath!,probe,host));
        Assert.Equal(11,original.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"identity\":true",original);
        Assert.Contains("BeforeOpen",original);
        Assert.Contains("ParameterBinding",original);
    }
}
