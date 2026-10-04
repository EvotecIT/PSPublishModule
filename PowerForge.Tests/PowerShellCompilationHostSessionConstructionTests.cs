using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void HostSessionConstruction_PreservesNativeTypeLookupArgumentsAndCookieStorage(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function New-OfflineSession {
                [CmdletBinding()]param($Trace,$Argument,[switch]$Invalid)
                $session='prior'
                try {
                    if($Invalid){$session=[Microsoft.PowerShell.Commands.WebRequestSession]::new($($Trace.Add('argument');$Argument))}
                    else{$session=[Microsoft.PowerShell.Commands.WebRequestSession]::new()}
                    $session.Cookies.Add([uri]'https://offline.invalid/',[Net.Cookie]::new('owned','value'))
                    $session.Cookies.GetCookies([uri]'https://offline.invalid/')[0].Value='changed'
                }catch{[pscustomobject]@{error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                finally{$Trace.Add('finally')}
                [pscustomobject]@{session=$session.GetType().FullName;prior=$session -is [string];trace=@($Trace.ToArray());cookies=if($session -isnot [string]){@($session.Cookies.GetCookies([uri]'https://offline.invalid/')|ForEach-Object {$_.Name+'='+$_.Value})}}
            }
            function New-ReturnedSession {[CmdletBinding()]param();return [Microsoft.PowerShell.Commands.WebRequestSession]::new()}
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.HostSession", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, entry => {
            Assert.True(entry.EmittedClrMethod);
            Assert.True(entry.UsesNativeFunctionBinding);
            Assert.False(entry.RetainedHostedSource);
        });
        const string probe = """
            foreach($loaded in $false,$true){
                if($loaded){Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop}
                foreach($invalid in $false,$true){foreach($argument in $null,42,'value'){
                    $trace=[Collections.Generic.List[string]]::new()
                    $records=@(New-OfflineSession -Trace $trace -Argument $argument -Invalid:$invalid)
                    [pscustomobject]@{loaded=$loaded;invalid=$invalid;records=$records}|ConvertTo-Json -Depth 8 -Compress
                }}
            }
            $first=New-ReturnedSession;$second=New-ReturnedSession
            $first.Cookies.Add([uri]'https://offline.invalid/',[Net.Cookie]::new('owned','value'))
            [pscustomobject]@{type=$first.GetType().FullName;same=[object]::ReferenceEquals($first,$second);first=$first.Cookies.Count;second=$second.Cookies.Count}|ConvertTo-Json -Compress
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        Assert.Equal(original, RunModuleProof(result.ArtifactPath!, probe, host));
        Assert.Equal(13, original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("owned=changed", original);
        Assert.Contains("\"same\":false", original);
        Assert.Contains("argument", original);
        Assert.Contains("prior\":true", original);
        // Each run starts a fresh host. A preceding JSON call or Utility import can
        // conceal a first-use type lookup failure in the generated invocation.
        for (var cold = 0; cold < 4; cold++)
            Assert.Equal(RunModuleProof(fixture.ScriptPath, probe, host), RunModuleProof(result.ArtifactPath!, probe, host));
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void HostSessionConstruction_KeepsRuntimeFreeAndOtherTypeConsumersClosed(string framework)
    {
        var source = PowerShellSourceParser.Parse("""
            function New-Session {[CmdletBinding()]param();[Microsoft.PowerShell.Commands.WebRequestSession]::new()}
            function New-Unknown {[CmdletBinding()]param();[Missing.Authored.Session]::new()}
            function Read-Type {[CmdletBinding()]param();[Microsoft.PowerShell.Commands.WebRequestSession]}
            function Read-TypeArgument {[CmdletBinding()]param();[psobject]::new([Microsoft.PowerShell.Commands.WebRequestSession])}
            """, Path.Combine(Path.GetTempPath(), "host-session-boundaries.ps1"));
        var hybrid = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal("New_Session", Assert.Single(hybrid.Emitted.Methods).GeneratedName);
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Empty(strict.Emitted.Methods);
    }
}
