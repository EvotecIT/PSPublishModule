using PowerForge;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void LocalRunspaceCleanup_PreservesAuthoredLifecycleAndBorrowedIdentity(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSScriptTools", "LocalRunspace", "remove-runspace.ps1");
        Assert.Equal("9F07A632413FAB809840D7C460FA2108E5DB2633D09B64081DFA24B575519451",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function Get-BorrowedRunspace {
                [CmdletBinding()]param([System.Management.Automation.Runspaces.Runspace]$Value,$Caller)
                [pscustomobject]@{same=[object]::ReferenceEquals($Value,$Caller);bound=[object]::ReferenceEquals($Value,$PSBoundParameters.Value);state=[string]$Value.RunspaceStateInfo.State;availability=[string]$Value.RunspaceAvailability}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RunspaceCleanup", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(2, built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries.Where(entry => entry.EmittedClrMethod), entry => {
            Assert.True(entry.UsesNativeFunctionBinding); Assert.False(entry.RetainedHostedSource);
        });
        const string probe = """
            $ErrorActionPreference='Stop'
            foreach($case in 'open','unopened','closed','disposed','whatif','id','busy'){
                $runspace=[runspacefactory]::CreateRunspace();$pipeline=$null;$started=$null;$release=$null
                try {
                    if($case -ne 'unopened'){$runspace.Open()}
                    if($case -in 'closed','disposed'){$runspace.Close()}
                    if($case -eq 'disposed'){$runspace.Dispose()}
                    if($case -eq 'busy'){
                        $started=[Threading.ManualResetEvent]::new($false);$release=[Threading.ManualResetEvent]::new($false)
                        $runspace.SessionStateProxy.SetVariable('started',$started);$runspace.SessionStateProxy.SetVariable('release',$release)
                        $pipeline=[PowerShell]::Create();$pipeline.Runspace=$runspace
                        [void]$pipeline.AddScript('[void]$started.Set();if(-not $release.WaitOne(5000)){throw "bounded wait expired"};"released"')
                        $pending=$pipeline.BeginInvoke()
                        if(-not $started.WaitOne(5000)){throw 'child failed to start'}
                    }
                    $before=Get-BorrowedRunspace -Value $runspace -Caller $runspace
                    $warnings=@();$failure=$null;$records=@()
                    try {
                        $records=@(if($case -eq 'id'){Remove-Runspace -ID $runspace.Id -Confirm:$false}
                            else{Remove-Runspace -Runspace $runspace -WhatIf:($case -eq 'whatif') -Confirm:$false -WarningVariable warnings 3>$null})
                    }catch{$failure=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                    $after=Get-BorrowedRunspace -Value $runspace -Caller $runspace
                    $reused=@()
                    if($case -eq 'whatif'){
                        $pipeline=[PowerShell]::Create();$pipeline.Runspace=$runspace;[void]$pipeline.AddScript('"reused"');$reused=@($pipeline.Invoke())
                    }
                    if($case -eq 'busy'){
                        [void]$release.Set()
                        if(-not $pending.AsyncWaitHandle.WaitOne(5000)){throw 'child failed to finish'}
                        $reused=@($pipeline.EndInvoke($pending))
                    }
                    [pscustomobject]@{case=$case;before=$before;after=$after;recordCount=$records.Count;warnings=$warnings.Count;failure=$failure;reused=$reused}|ConvertTo-Json -Depth 5 -Compress
                }finally{
                    if($release){[void]$release.Set()}
                    if($pipeline){$pipeline.Stop();$pipeline.Dispose()}
                    $runspace.Close();$runspace.Dispose()
                    if($started){$started.Dispose()};if($release){$release.Dispose()}
                }
            }
            $spaces=@([runspacefactory]::CreateRunspace(),[runspacefactory]::CreateRunspace())
            try{
                foreach($space in $spaces){$space.Open()}
                $records=@($spaces|Remove-Runspace -Confirm:$false)
                [pscustomobject]@{case='pipeline';states=@($spaces|ForEach-Object {[string]$_.RunspaceStateInfo.State});records=$records.Count}|ConvertTo-Json -Compress
            }finally{foreach($space in $spaces){$space.Close();$space.Dispose()}}
            foreach($case in 'null','missing-id'){
                try{if($case -eq 'null'){Remove-Runspace -Runspace $null -Confirm:$false}else{Remove-Runspace -ID ([int]::MaxValue) -Confirm:$false}}
                catch{[pscustomobject]@{case=$case;id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}|ConvertTo-Json -Compress}
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        Assert.Equal(original, RunModuleProof(built.ArtifactPath!, probe, host));
        var observations = original.Split(Environment.NewLine).Where(line => line.StartsWith('{'))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        Assert.Equal(10, observations.Length);
        foreach (var row in observations.Take(7))
        {
            var name = row.GetProperty("case").GetString();
            Assert.True(row.GetProperty("before").GetProperty("same").GetBoolean());
            Assert.True(row.GetProperty("after").GetProperty("same").GetBoolean());
            Assert.True(row.GetProperty("before").GetProperty("bound").GetBoolean());
            Assert.True(row.GetProperty("after").GetProperty("bound").GetBoolean());
            Assert.Equal(name is "busy" or "whatif" ? "Opened" : "Closed",
                row.GetProperty("after").GetProperty("state").GetString());
            Assert.Equal(0, row.GetProperty("recordCount").GetInt32());
            Assert.Equal(name == "busy" ? 2 : 0, row.GetProperty("warnings").GetInt32());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("failure").ValueKind);
        }
        Assert.All(observations.Skip(8), row => Assert.False(string.IsNullOrEmpty(row.GetProperty("id").GetString())));
        Assert.Contains("\"same\":true", original);
        Assert.Contains("\"reused\":[\"reused\"]", original);
        Assert.Contains("\"reused\":[\"released\"]", original);
        Assert.Contains("\"states\":[\"Closed\",\"Closed\"]", original);
        Assert.Contains("\"warnings\":2", original);
    }
}
