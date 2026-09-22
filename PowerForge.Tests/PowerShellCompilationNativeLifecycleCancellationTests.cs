namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> NativeFunctionAndScriptBlockHosts()
        => StatementErrorHosts().SelectMany(host => new[] { false, true }.Select(block => new object[] { host[0], host[1], block }));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativeFunctionAndScriptBlockHosts))]
    public void NativeLifecycle_CancellationPreservesFinallyCleanAndRunspaceReuse(string framework, string host, bool scriptBlock)
    {
        var clean = framework == "net472" ? "" : "clean { $State.Clean() }";
        var source = """
            function Invoke-LifecycleStop {
                [CmdletBinding()] param([object]$State,[string]$Phase)
                begin { if($Phase -eq 'begin') { try { $State.Enter(); while($State.Pause) { }; 'begin' } finally { $State.Finish() } } }
                process { if($Phase -eq 'process') { try { $State.Enter(); while($State.Pause) { }; 'process' } finally { $State.Finish() } } }
                end { if($Phase -eq 'end') { try { $State.Enter(); while($State.Pause) { }; 'end' } finally { $State.Finish() } } }
            CLEAN_CLAUSE
            }
            """.Replace("CLEAN_CLAUSE", clean);
        if (scriptBlock)
            source = source.Replace("begin {", "$block={ begin {");
        if (scriptBlock)
            source += "\n& $block\n}";
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeLifecycleStop", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 1, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        if (scriptBlock) Assert.Single(Assert.Single(result.Manifest.UnitDispositionLedger.Entries).RegionGraph!.ScriptBlocks);
        const string probe = AcknowledgedStopProbe + """
            Add-Type -TypeDefinition @'
            using System.Threading;
            public sealed class NativeLifecycleStopState {
                public readonly ManualResetEvent Started = new ManualResetEvent(false);
                public readonly ManualResetEvent Release = new ManualResetEvent(false);
                public bool Pause = true;
                public int Calls, Finally, Cleans;
                public void Enter() { Calls++; Started.Set(); if(Pause && !Release.WaitOne(5000)) throw new System.TimeoutException(); }
                public void Finish() { Finally++; }
                public void Clean() { Cleans++; }
            }
            '@
            foreach($phase in 'begin','process','end') {
                $state=[NativeLifecycleStopState]::new(); $ps=[powershell]::Create()
                try {
                    [void]$ps.AddScript({param($path) $module=Import-Module $path -PassThru; $name=$module.Name+'\Invoke-LifecycleStop'; if((Get-Command $name).Module.Path -ne $module.Path) {throw 'Wrong module'}; $name}).AddArgument($modulePath)
                    $names=@($ps.Invoke()); if($ps.HadErrors -or $names.Count -ne 1) {throw 'Import failed'}
                    $name=[string]$names[0]; $ps.Commands.Clear()
                    [void]$ps.AddCommand($name).AddParameter('State',$state).AddParameter('Phase',$phase)
                    $running=$ps.BeginInvoke()
                    if(!$state.Started.WaitOne(5000)) {throw ('Clause was not entered: '+$ps.Streams.Error)}
                    $stop=Start-CompilerTestStop $ps
                    [void]$state.Release.Set(); $ps.EndStop($stop)
                    try {[void]$ps.EndInvoke($running)} catch {}
                    [pscustomobject]@{phase=$phase;state=[string]$ps.InvocationStateInfo.State;calls=$state.Calls;finally=$state.Finally;clean=$state.Cleans;
                        errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)} | ConvertTo-Json -Compress
                    $ps.Commands.Clear(); $ps.Streams.Error.Clear(); $state.Pause=$false; $state.Calls=0; $state.Finally=0; $state.Cleans=0
                    [void]$ps.AddCommand($name).AddParameter('State',$state).AddParameter('Phase',$phase)
                    $records=@($ps.Invoke())
                    [pscustomobject]@{phase=$phase;state=[string]$ps.InvocationStateInfo.State;calls=$state.Calls;finally=$state.Finally;clean=$state.Cleans;
                        records=$records;errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)} | ConvertTo-Json -Compress
                } finally { [void]$state.Release.Set(); $ps.Dispose(); $state.Started.Dispose(); $state.Release.Dispose() }
            }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-lifecycle-stop");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-lifecycle-stop");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(6, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(3, original.StandardOutput.Split('\n').Count(line => line.Contains("\"state\":\"Stopped\",\"calls\":1,\"finally\":1", StringComparison.Ordinal)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
