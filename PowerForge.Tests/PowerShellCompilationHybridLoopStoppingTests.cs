using PowerForge;
using System.Text.RegularExpressions;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HybridLoopRegions_ObserveNativeStoppingAndAllowLaterInvocation(string framework, string host)
    {
        const string source = """
            function Invoke-HybridLoop([double]$Limit) {
                $value=0.0
                while($value -lt $Limit) { $value += 1.0 }
                return $value
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.HybridLoopStops", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var module = File.ReadAllText(result.ArtifactPath!);
        var helper = Regex.Match(module, @"\[([A-Za-z0-9_.]+)\]::(__PowerForgeRegion_[A-Za-z0-9_]+)\(");
        Assert.True(helper.Success, module);
        Assert.Contains("CreateLoopInterrupt($ExecutionContext)", module, StringComparison.Ordinal);
        var plan = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.RegionEvidence", "Methods", framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.True(Assert.Single(plan.PromotedRegions).RequiresPowerShellStopping);
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections.Generic;
            using System.Reflection;
            using System.Runtime.ExceptionServices;
            using System.Threading;
            public static class HybridLoopStopProbe {
                public static object Invoke(Type owner, string name, Action nativeCheck,
                    ManualResetEvent started, ManualResetEvent release, List<object> observed) {
                    bool entered = false;
                    Action checkpoint = () => {
                        if(!entered) { entered=true; observed.Add("entered"); started.Set(); release.WaitOne(); }
                        nativeCheck();
                    };
                    try { return owner.GetMethod(name).Invoke(null, new object[] { double.MaxValue, checkpoint }); }
                    catch(TargetInvocationException error) {
                        if(error.InnerException != null) ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                        throw;
                    }
                }
            }
            '@
            $started=[Threading.ManualResetEvent]::new($false)
            $release=[Threading.ManualResetEvent]::new($false)
            $observed=[Collections.Generic.List[object]]::new()
            $ps=[powershell]::Create()
            try {
                [void]$ps.AddCommand('Import-Module').AddParameter('Name',$modulePath)
                [void]$ps.Invoke(); $ps.Commands.Clear()
                $script={ param($compiled,$owner,$member,$started,$release,$observed)
                    try {
                        if($compiled) {
                            $check=[PowerForge.Generated.Runtime.PowerShellStatementErrorContext]::CreateLoopInterrupt($ExecutionContext)
                            [HybridLoopStopProbe]::Invoke([type]$owner,$member,$check,$started,$release,$observed)
                        } else {
                            $observed.Add('entered'); [void]$started.Set(); [void]$release.WaitOne()
                            Invoke-HybridLoop ([double]::MaxValue)
                        }
                    } catch { $observed.Add('caught') }
                    finally { $observed.Add('finally') }
                }
                [void]$ps.AddScript($script.ToString()).AddArgument($useCompiled).AddArgument($owner).AddArgument($member).AddArgument($started).AddArgument($release).AddArgument($observed)
                $running=$ps.BeginInvoke()
                if(-not $started.WaitOne(5000)) { throw "No loop entry: $($ps.Streams.Error)" }
                $stop=$ps.BeginStop($null,$null)
                $deadline=[DateTime]::UtcNow.AddSeconds(5)
                while($ps.InvocationStateInfo.State -ne 'Stopping' -and [DateTime]::UtcNow -lt $deadline) { [Threading.Thread]::Sleep(1) }
                if($ps.InvocationStateInfo.State -ne 'Stopping') { throw 'The stop request was not observed.' }
                [void]$release.Set()
                if(-not $stop.AsyncWaitHandle.WaitOne(5000)) { throw 'The generated loop ignored native stopping.' }
                $ps.EndStop($stop)
                try { [void]$ps.EndInvoke($running) } catch [Management.Automation.PipelineStoppedException] {}
                [string]$ps.InvocationStateInfo.State
                $observed.ToArray()
                $ps.Commands.Clear()
                [void]$ps.AddScript('Invoke-HybridLoop 4.0')
                $later=@($ps.Invoke())
                'later='+$later.Count+'/'+$later[0]+'/'+$ps.Streams.Error.Count
            } finally {
                [void]$release.Set()
                $ps.Dispose(); $started.Dispose(); $release.Dispose()
            }
            """;
        var original = RunStatementErrorProbe(host, "$useCompiled=$false; $modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-hybrid-loop-stop");
        var compiled = RunStatementErrorProbe(host, "$useCompiled=$true; $owner='" + helper.Groups[1].Value + "'; $member='" + helper.Groups[2].Value +
            "'; $modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-hybrid-loop-stop");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("Stopped", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("later=1/4/0", original.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("caught", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
