using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HybridGuardedLoop_StopsThroughExportedFunctionAndAllowsReuse(string framework, string host)
    {
        const string source = """
            function Invoke-GuardedLoop([double] $Limit) {
                $value = 0.0
                while ($value -lt $Limit) { $value += 1.0 }
                data LoopBarrier { 'retained' }
                & { $value }
            }
            Export-ModuleMember -Function Invoke-GuardedLoop
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var plan = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.GuardedLoop", "Methods", framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.True(plan.PromotedRegions.Length > 0, System.Text.Json.JsonSerializer.Serialize(plan.RegionCandidates) + System.Text.Json.JsonSerializer.Serialize(plan.Diagnostics));
        var region = Assert.Single(plan.PromotedRegions);
        Assert.True(region.RequiresLocalOwnershipGuard);
        Assert.True(region.RequiresPowerShellStopping);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.GuardedLoopStops", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        // Instrument the generated callback factory, leaving the exported invocation and helper intact.
        // Entry is signalled from the helper's first actual loop checkpoint, not before calling it.
        var module = File.ReadAllText(result.ArtifactPath!);
        const string factory = "[PowerForge.Generated.Runtime.PowerShellStatementErrorContext]::CreateLoopInterrupt($ExecutionContext)";
        Assert.Contains(factory, module, StringComparison.Ordinal);
        File.WriteAllText(result.ArtifactPath!, module.Replace(factory, "[GuardedLoopProbe]::Wrap(" + factory + ")", StringComparison.Ordinal));
        File.WriteAllText(fixture.ScriptPath, source.Replace("{ $value += 1.0 }", "{ [GuardedLoopProbe]::Checkpoint(); $value += 1.0 }", StringComparison.Ordinal));
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Threading;
            public static class GuardedLoopProbe {
                public static readonly ManualResetEvent Started = new ManualResetEvent(false);
                public static readonly ManualResetEvent Release = new ManualResetEvent(false);
                public static int Entries;
                public static void Checkpoint() {
                    if (Interlocked.Increment(ref Entries) == 1) { Started.Set(); Release.WaitOne(); }
                }
                public static Action Wrap(Action check) { return () => { Checkpoint(); check(); }; }
            }
            '@
            $ps = [powershell]::Create()
            $observed = [Collections.Generic.List[string]]::new()
            try {
                [void]$ps.AddCommand('Import-Module').AddParameter('Name', $modulePath)
                [void]$ps.Invoke(); $ps.Commands.Clear()
                $script = {
                    param($observed)
                    try { Invoke-GuardedLoop ([double]::MaxValue) }
                    catch { $observed.Add('caught') }
                    finally { $observed.Add('finally') }
                }
                [void]$ps.AddScript($script.ToString()).AddArgument($observed)
                $running = $ps.BeginInvoke()
                if (-not [GuardedLoopProbe]::Started.WaitOne(10000)) { throw "No actual loop entry: $($ps.Streams.Error)" }
                $stop = $ps.BeginStop($null, $null)
                $deadline = [DateTime]::UtcNow.AddSeconds(5)
                while ($ps.InvocationStateInfo.State -ne 'Stopping' -and [DateTime]::UtcNow -lt $deadline) { [Threading.Thread]::Sleep(1) }
                if ($ps.InvocationStateInfo.State -ne 'Stopping') { throw 'Stop request not observed.' }
                [void][GuardedLoopProbe]::Release.Set()
                if (-not $stop.AsyncWaitHandle.WaitOne(5000)) { throw 'Loop ignored stopping.' }
                $ps.EndStop($stop)
                try { [void]$ps.EndInvoke($running) } catch [Management.Automation.PipelineStoppedException] {}
                [string]$ps.InvocationStateInfo.State
                'entries=' + [GuardedLoopProbe]::Entries
                $observed.ToArray()
                $ps.Commands.Clear()
                [void]$ps.AddScript('Invoke-GuardedLoop 4.0')
                $later = @($ps.Invoke())
                'later=' + $later.Count + '/' + $later[0] + '/' + $ps.Streams.Error.Count
            } finally {
                [void][GuardedLoopProbe]::Release.Set()
                $ps.Dispose()
                [GuardedLoopProbe]::Started.Dispose(); [GuardedLoopProbe]::Release.Dispose()
            }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-guarded-loop-stop");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-guarded-loop-stop");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("Stopped", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("entries=1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("later=1/4/0", original.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("caught", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
