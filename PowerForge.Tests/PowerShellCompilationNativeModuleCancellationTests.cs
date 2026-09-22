namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeModuleState_CancellationPreservesRetainedCleanupAndReuse(string framework, string host)
    {
        var clean = framework == "net472" ? "" : "clean { $script:Count += 100; $Signal.Clean() }";
        using var fixture = ArtifactFixture.Create("""
            $script:Count=0
            function Invoke-OwnedStop {
                [CmdletBinding()] param([object]$Signal)
                begin { $script:Count=0 }
                process {
                    try {
                        $script:Count=1
                        Wait-RetainedSignal -Signal $Signal
                        $script:Count=2
                        'done'
                    } finally { $script:Count+=10; $Signal.Finish() }
                }
            CLEAN_CLAUSE
            }
            function Wait-RetainedSignal {
                [CmdletBinding()] param([object]$Signal)
                dynamicparam { }
                process { $Signal.Enter(); while($Signal.Pause) { } }
            }
            function Get-OwnedCount { [CmdletBinding()] param() return $script:Count -join ',' }
            Export-ModuleMember -Function Invoke-OwnedStop, Get-OwnedCount
            """.Replace("CLEAN_CLAUSE", clean), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeOwnedStop", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var units = result.Manifest!.UnitDispositionLedger!.Entries;
        Assert.False(Assert.Single(units, unit => unit.Name == "Invoke-OwnedStop").RetainedHostedSource);
        Assert.False(Assert.Single(units, unit => unit.Name == "Get-OwnedCount").RetainedHostedSource);
        Assert.True(Assert.Single(units, unit => unit.Name == "Wait-RetainedSignal").RetainedHostedSource);
        const string probe = AcknowledgedStopProbe + """
            Add-Type -TypeDefinition @'
            using System.Threading;
            public sealed class OwnedStopSignal {
                public readonly ManualResetEvent Started = new ManualResetEvent(false);
                public readonly ManualResetEvent Release = new ManualResetEvent(false);
                public bool Pause = true;
                public int Calls, Finally, Cleans;
                public void Enter() { Calls++; Started.Set(); if(Pause && !Release.WaitOne(5000)) throw new System.TimeoutException(); }
                public void Finish() { Finally++; }
                public void Clean() { Cleans++; }
            }
            '@
            $state=[OwnedStopSignal]::new(); $ps=[powershell]::Create()
            try {
                [void]$ps.AddScript({param($path) $module=Import-Module $path -PassThru; $module.Name}).AddArgument($modulePath)
                $names=@($ps.Invoke()); if($ps.Streams.Error.Count -ne 0 -or $names.Count -ne 1) {throw 'Import failed'}
                $name=[string]$names[0]; $ps.Commands.Clear()
                [void]$ps.AddCommand($name+'\Invoke-OwnedStop').AddParameter('Signal',$state)
                $running=$ps.BeginInvoke()
                if(!$state.Started.WaitOne(5000)) {throw ('Retained stage was not entered: '+$ps.Streams.Error)}
                $stop=Start-CompilerTestStop $ps
                [void]$state.Release.Set(); $ps.EndStop($stop)
                try {[void]$ps.EndInvoke($running)} catch {}
                $stopped=[string]$ps.InvocationStateInfo.State
                $errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)
                $ps.Commands.Clear(); $ps.Streams.Error.Clear()
                [void]$ps.AddCommand($name+'\Get-OwnedCount'); $count=@($ps.Invoke())
                if($ps.Streams.Error.Count -ne 0) {throw 'State read failed'}
                [pscustomobject]@{state=$stopped;count=@($count | ForEach-Object {[string]$_});calls=$state.Calls;finally=$state.Finally;clean=$state.Cleans;errors=$errors} | ConvertTo-Json -Compress
                $ps.Commands.Clear(); $state.Pause=$false
                [void]$ps.AddCommand($name+'\Invoke-OwnedStop').AddParameter('Signal',$state); $values=@($ps.Invoke())
                if($ps.Streams.Error.Count -ne 0) {throw 'Reuse failed'}
                $ps.Commands.Clear(); [void]$ps.AddCommand($name+'\Get-OwnedCount'); $count=@($ps.Invoke())
                [pscustomobject]@{state=[string]$ps.InvocationStateInfo.State;count=@($count | ForEach-Object {[string]$_});values=@($values | ForEach-Object {[string]$_});calls=$state.Calls;finally=$state.Finally;clean=$state.Cleans} | ConvertTo-Json -Compress
            } finally { $ps.Dispose(); $state.Started.Dispose(); $state.Release.Dispose() }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-owned-stop");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-owned-stop");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(2, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
