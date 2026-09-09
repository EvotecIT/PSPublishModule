namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void MultipleRegions_PreserveRetainedMutationAndSequentialCaptures(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            $script:Trace = [Collections.Generic.List[string]]::new()
            function Invoke-RegionFlow {
                [CmdletBinding()] param([ValidateNotNull()][string]$Value)
                $script:Trace.Clear()
                $first = 'first:'+ $Value
                $script:Trace.Add($first)
                $captured = Invoke-RetainedRegion -Value $first
                $second = $captured + ':second'
                $script:Trace.Add($second)
                $again = Invoke-RetainedRegion -Value $second
                $script:Trace.Add('last:'+ $again)
                $script:Trace -join '|'
            }
            function Invoke-RetainedRegion {
                [CmdletBinding()] param([string]$Value)
                dynamicparam { }
                process {
                    $script:Trace.Add('retained:'+ $Value)
                    $Value.ToUpperInvariant()
                }
            }
            Export-ModuleMember -Function Invoke-RegionFlow
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.MultipleRegions", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var units = result.Manifest!.UnitDispositionLedger!.Entries;
        var flow = Assert.Single(units, unit => unit.Name == "Invoke-RegionFlow");
        Assert.False(flow.RetainedHostedSource);
        var graph = Assert.IsType<PowerShellCompilationRegionGraph>(flow.RegionGraph);
        Assert.Equal(2, graph.Regions.Sum(region => region.HostedCommandBoundarySites));
        Assert.True(graph.Regions.Count > 2);
        Assert.Contains(graph.Regions.SelectMany(region => region.Inputs), input => input == "PowerShellSessionVariable:SCRIPT:TRACE");
        Assert.Contains(graph.Regions.SelectMany(region => region.Mutations), mutation => mutation == "PowerShellSessionVariable:CAPTURED");
        Assert.Contains(graph.Regions.SelectMany(region => region.Outputs), output => output == "PowerShellSessionState:*");
        Assert.Contains(graph.Regions.SelectMany(region => region.Errors), error => error == "PowerShellSessionStateError");
        var initialRegion = graph.Regions[0];
        Assert.Contains("PowerShellSessionVariable:SCRIPT:TRACE", initialRegion.Mutations);
        Assert.Contains("PowerShellSessionVariable:SCRIPT:TRACE", initialRegion.Outputs);
        Assert.True(Assert.Single(units, unit => unit.Name == "Invoke-RetainedRegion").RetainedHostedSource);
        const string probe = "Import-Module $modulePath -Force; Invoke-RegionFlow 'alpha'; Invoke-RegionFlow ''";
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-multiple-regions");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-multiple-regions");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("first:alpha|retained:first:alpha|FIRST:ALPHA:second|retained:FIRST:ALPHA:second|last:FIRST:ALPHA:SECOND", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
    }
}
