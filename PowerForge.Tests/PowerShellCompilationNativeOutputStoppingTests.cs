using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeOutput_StopsEnumerationDisposesAndRunsFinally(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-NativeOutputStop { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value,[object]$Marker); try { $Value } finally { "cleanup=$Marker" } }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeOutputStopping", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 1, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => cause.Message))));
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            using System.Threading;
            public sealed class NativeOutputStopInput : IEnumerable {
                public readonly ManualResetEvent Started=new ManualResetEvent(false);
                public readonly ManualResetEvent Release=new ManualResetEvent(false);
                public bool Pause=true;
                public int Disposals;
                public int Cleanups;
                public IEnumerator GetEnumerator() { return new Cursor(this); }
                public object GetMarker() { return new Marker(this); }
                private sealed class Marker {
                    private readonly NativeOutputStopInput owner;
                    public Marker(NativeOutputStopInput owner) { this.owner=owner; }
                    public override string ToString() { owner.Cleanups++; return "marker"; }
                }
                private sealed class Cursor : IEnumerator, IDisposable {
                    private readonly NativeOutputStopInput owner;
                    private int index=-1;
                    public Cursor(NativeOutputStopInput owner) { this.owner=owner; }
                    public bool MoveNext() {
                        if(owner.Pause) { owner.Pause=false; owner.Started.Set(); if(!owner.Release.WaitOne(5000)) throw new InvalidOperationException("Enumeration was not released."); }
                        return ++index<3;
                    }
                    public object Current { get { return index; } }
                    public void Reset() { throw new NotSupportedException(); }
                    public void Dispose() { owner.Disposals++; }
                }
            }
            '@
            $inputValue=[NativeOutputStopInput]::new(); $marker=$inputValue.GetMarker(); $ps=[powershell]::Create()
            try {
                [void]$ps.AddCommand('Import-Module').AddParameter('Name',$modulePath)
                [void]$ps.Invoke(); $ps.Commands.Clear()
                [void]$ps.AddCommand('Invoke-NativeOutputStop').AddParameter('Value',$inputValue).AddParameter('Marker',$marker)
                $running=$ps.BeginInvoke()
                if (-not $inputValue.Started.WaitOne(5000)) { throw "Native output did not reach enumeration. $($ps.Streams.Error)" }
                $stop=$ps.BeginStop($null,$null)
                $deadline=[DateTime]::UtcNow.AddSeconds(5)
                while ($ps.InvocationStateInfo.State -ne 'Stopping' -and [DateTime]::UtcNow -lt $deadline) { [Threading.Thread]::Sleep(1) }
                if ($ps.InvocationStateInfo.State -ne 'Stopping') { throw 'The stop request was not observed.' }
                [void]$inputValue.Release.Set()
                if (-not $stop.AsyncWaitHandle.WaitOne(5000)) { throw 'Native output did not stop.' }
                $ps.EndStop($stop)
                try { [void]$ps.EndInvoke($running) } catch [Management.Automation.PipelineStoppedException] {}
                [pscustomobject]@{state=[string]$ps.InvocationStateInfo.State;disposed=$inputValue.Disposals;cleanups=$inputValue.Cleanups;
                    errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)} | ConvertTo-Json -Compress
                $ps.Commands.Clear(); $ps.Streams.Error.Clear(); $inputValue.Disposals=0; $inputValue.Cleanups=0
                [void]$ps.AddCommand('Invoke-NativeOutputStop').AddParameter('Value',$inputValue).AddParameter('Marker',$marker)
                $records=@($ps.Invoke())
                [pscustomobject]@{state=[string]$ps.InvocationStateInfo.State;disposed=$inputValue.Disposals;cleanups=$inputValue.Cleanups;records=$records;
                    errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)} | ConvertTo-Json -Compress
            } finally { [void]$inputValue.Release.Set(); $ps.Dispose(); $inputValue.Started.Dispose(); $inputValue.Release.Dispose() }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-output-stop-probe");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-output-stop-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardOutput + original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"state\":\"Stopped\",\"disposed\":1,\"cleanups\":1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"Completed\",\"disposed\":1,\"cleanups\":1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
