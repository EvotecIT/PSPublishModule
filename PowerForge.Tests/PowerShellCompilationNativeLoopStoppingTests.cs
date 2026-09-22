using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeLoops_ObserveStoppingRunFinallyAndAllowAnotherInvocation(string framework, string host)
    {
        const string source = """
            function Invoke-NativeStop {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Marker)
                try { for ($i=0; $i -lt 3; $i++) { "$Marker" } }
                finally { "cleanup=$Marker" }
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeLoopStopping", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 1, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => cause.Message))));
        const string probe = AcknowledgedStopProbe + """
            Add-Type -TypeDefinition @'
            using System;
            using System.Threading;
            public sealed class NativeLoopStopMarker {
                public readonly ManualResetEvent Started = new ManualResetEvent(false);
                public readonly ManualResetEvent Release = new ManualResetEvent(false);
                public int Calls;
                private bool pause = true;
                public override string ToString() {
                    Interlocked.Increment(ref Calls);
                    if (pause) {
                        pause = false;
                        Started.Set();
                        if (!Release.WaitOne(5000)) throw new InvalidOperationException("The marker was not released.");
                    }
                    return "marker";
                }
            }
            '@
            $marker=[NativeLoopStopMarker]::new()
            $started=$marker.Started
            $release=$marker.Release
            $ps=[powershell]::Create()
            try {
                [void]$ps.AddCommand('Import-Module').AddParameter('Name',$modulePath)
                [void]$ps.Invoke(); $ps.Commands.Clear()
                [void]$ps.AddCommand('Invoke-NativeStop').AddParameter('Marker',$marker)
                $running=$ps.BeginInvoke()
                if (-not $started.WaitOne(5000)) { throw "The native loop did not reach its pause. $($ps.Streams.Error)" }
                $stop=Start-CompilerTestStop $ps
                [void]$release.Set()
                if (-not $stop.AsyncWaitHandle.WaitOne(5000)) { throw 'The finite native loop did not stop.' }
                $ps.EndStop($stop)
                try { [void]$ps.EndInvoke($running) } catch [Management.Automation.PipelineStoppedException] {}
                [pscustomobject]@{state=[string]$ps.InvocationStateInfo.State;calls=$marker.Calls;errors=@($ps.Streams.Error | ForEach-Object { $_.FullyQualifiedErrorId })} | ConvertTo-Json -Compress
                $ps.Commands.Clear(); $ps.Streams.Error.Clear(); $marker.Calls=0
                [void]$ps.AddCommand('Invoke-NativeStop').AddParameter('Marker',$marker)
                $records=@($ps.Invoke())
                [pscustomobject]@{state=[string]$ps.InvocationStateInfo.State;calls=$marker.Calls;records=$records;errors=@($ps.Streams.Error | ForEach-Object { $_.FullyQualifiedErrorId })} | ConvertTo-Json -Compress
            } finally {
                [void]$release.Set()
                $ps.Dispose(); $started.Dispose(); $release.Dispose()
            }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-loop-stop-probe");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-loop-stop-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardOutput + original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"state\":\"Stopped\",\"calls\":2", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"Completed\",\"calls\":4", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
