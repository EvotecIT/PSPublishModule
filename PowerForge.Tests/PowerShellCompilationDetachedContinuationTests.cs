using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void Build_HybridSecondDetachedRegionTransfersMultipleLocalsAndPreservesHostedContinuation(
        string framework,
        string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-DetachedReport {
                [CmdletBinding()]
                param([int] $Seed, [string] $Mode)
                [int] $Count = $Seed
                [string] $Trace = "prefix:$Count"
                data PrefixBarrier { }
                & {
                    "middle:$Trace"
                    if ($Mode -eq 'error') { Write-Error 'middle-error' }
                }
                data MiddleBarrier { }
                $Trace = "$Trace|typed:$Count"
                $Count = $Seed
                data TailBarrier { }
                & { "tail:${Trace}:$Count" }
            }
            Export-ModuleMember -Function Get-DetachedReport
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "DetachedRegionMethods", framework,
            PowerShellCompilationCapabilities.HybridModule);
        var promoted = typed.PromotedRegions.OrderBy(static region => region.StartOffset).ToArray();
        Assert.True(promoted.Length == 2,
            "Promoted: " + promoted.Length + Environment.NewLine +
            string.Join(Environment.NewLine, typed.Diagnostics.Select(static item => item.Code + ": " + item.Message)) + Environment.NewLine +
            string.Join(Environment.NewLine, typed.RegionCandidates.Select(static item => item.DecisionCode + ": " + item.Reason)));
        Assert.True(promoted[0].RequiresLocalOwnershipGuard);
        Assert.Empty(promoted[0].InputLocals);
        Assert.False(promoted[1].RequiresLocalOwnershipGuard);
        Assert.Equal(new[] { "Count", "Trace" }, promoted[1].InputLocals.Select(static local => local.Name));
        Assert.Equal(new[] { "Count", "Trace" }, promoted[1].ContinuationLocals.Select(static local => local.Name));
        Assert.All(promoted[1].InputLocals, static local => Assert.True(local.HasTypeConstraint));
        var graph = Assert.Single(promoted[1].RegionGraph.Regions);
        Assert.True(
            new[] { "Local:Count", "Local:Trace", "Parameter:Seed" }.SequenceEqual(
                graph.Inputs.OrderBy(static input => input, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase));
        Assert.Contains(graph.Inputs, static input => input.Equals("Local:Count", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(graph.Inputs, static input => input.Equals("Local:Trace", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            new[] { "transfer:Local:Count", "transfer:Local:Trace" },
            graph.Outputs.OrderBy(static output => output, StringComparer.OrdinalIgnoreCase));
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<PowerShellCompiledRegion>(
            System.Text.Json.JsonSerializer.Serialize(promoted[1]));
        Assert.Equal(new[] { "Count", "Trace" }, roundTrip!.InputLocals.Select(static local => local.Name));

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DetachedContinuation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.PromotedTypedRegions);
        const string probe = """
            $ErrorActionPreference = 'Continue'
            $continuedErrors = @()
            $continued = @(Get-DetachedReport -Seed 7 -Mode error -ErrorAction Continue -ErrorVariable continuedErrors 2>$null)
            'continue=' + ($continued -join ',') + '/errors=' + $continuedErrors.Count
            $stopped = [Collections.Generic.List[object]]::new()
            try {
                Get-DetachedReport -Seed 8 -Mode error -ErrorAction Stop 2>$null | ForEach-Object { [void]$stopped.Add($_) }
            } catch {
                'stop=' + ($stopped.ToArray() -join ',') + '/caught=' + $_.FullyQualifiedErrorId
            }
            $reuseErrors = @()
            $reuse = @(Get-DetachedReport -Seed 9 -Mode normal -ErrorAction Continue -ErrorVariable reuseErrors 2>$null)
            'reuse=' + ($reuse -join ',') + '/errors=' + $reuseErrors.Count
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath,
            "original-detached-continuation");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath,
            "compiled-detached-continuation");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("continue=middle:prefix:7,tail:prefix:7|typed:7:7/errors=1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("stop=middle:prefix:8/caught=", original.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("tail:prefix:8", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("reuse=middle:prefix:9,tail:prefix:9|typed:9:9/errors=0", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_HybridDetachedRegionRejectsUnconstrainedLocalTransfer()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-UnconstrainedDetachedReport {
                [CmdletBinding()]
                param([int] $Seed)
                [int] $Count = $Seed
                $Trace = "prefix:$Count"
                data PrefixBarrier { }
                & { "middle:$Trace" }
                data MiddleBarrier { }
                $Trace = "$Trace|typed"
                $Count = $Seed
                data TailBarrier { }
                & { "${Trace}:$Count" }
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "RejectedDetachedRegionMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.True(typed.PromotedRegions.Count() == 1,
            string.Join(Environment.NewLine, typed.Diagnostics.Select(static item => item.Code + ": " + item.Message)) + Environment.NewLine +
            string.Join(Environment.NewLine, typed.RegionCandidates.Select(static item => item.DecisionCode + ": " + item.Reason)));
        Assert.DoesNotContain(typed.PromotedRegions, static region => region.InputLocals.Count > 0);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void Build_HybridDetachedRegionRechecksInheritedLocalStorageAtItsBoundary(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-DetachedStorageReport {
                [CmdletBinding()]
                param([int] $Seed, [string] $Storage)
                [int] $Count = $Seed
                [string] $Trace = "prefix:$Count"
                data PrefixBarrier { }
                & { $null = $Storage }
                data MiddleBarrier { }
                $Trace = "$Trace|typed:$Count"
                $Count = $Seed
                data TailBarrier { }
                & { "tail:${Trace}:$Count" }
            }
            Export-ModuleMember -Function Get-DetachedStorageReport
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "DetachedStorageMethods", framework,
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(2, typed.PromotedRegions.Count());
        Assert.Equal(2, typed.PromotedRegions.Single(static region => region.InputLocals.Count > 0).InputLocals.Count);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.DetachedStorage",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.PromotedTypedRegions);
        const string probe = """
            $observations = foreach ($storage in @('Normal', 'ReadOnly')) {
                & $module {
                    param($storage)
                    Remove-Variable -Scope Script -Name Count -Force -ErrorAction SilentlyContinue
                    if ($storage -eq 'ReadOnly') {
                        New-Variable -Scope Script -Name Count -Value 40 -Option AllScope,ReadOnly
                    }
                } $storage
                $Error.Clear()
                $values = @(Get-DetachedStorageReport -Seed 7 -Storage $storage -ErrorAction Continue 2>$null)
                [pscustomobject]@{
                    Storage = $storage
                    Values = $values
                    Errors = @($Error | ForEach-Object {
                        [pscustomobject]@{
                            Id = $_.FullyQualifiedErrorId
                            Line = $_.InvocationInfo.ScriptLineNumber
                            Column = $_.InvocationInfo.OffsetInLine
                            Text = $_.InvocationInfo.Line.TrimEnd()
                        }
                    })
                }
            }
            & $module { Remove-Variable -Scope Script -Name Count -Force -ErrorAction SilentlyContinue }
            ConvertTo-Json -InputObject @($observations) -Depth 6 -Compress
            """;
        var original = RunStatementErrorProbe(host,
            "$module = Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "' -PassThru; " + probe,
            fixture.RootPath,
            "original-detached-storage");
        var compiled = RunStatementErrorProbe(host,
            "$module = Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "' -PassThru; " + probe,
            fixture.RootPath,
            "compiled-detached-storage");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var observations = System.Text.Json.Nodes.JsonNode.Parse(original.StandardOutput)!.AsArray();
        Assert.Equal(2, observations.Count);
        Assert.Empty(observations[0]!["Errors"]!.AsArray());
        Assert.NotEmpty(observations[1]!["Errors"]!.AsArray());
        Assert.Contains("tail:prefix:7|typed:7:7", observations[0]!["Values"]![0]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
