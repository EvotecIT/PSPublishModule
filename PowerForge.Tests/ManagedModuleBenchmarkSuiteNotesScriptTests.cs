using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkSuiteNotesScriptTests
{
    [Fact]
    public void SuiteNotes_SeparatesScoreboardsFromDiagnostics()
    {
        using var temp = new TemporaryDirectory();
        var notesPath = Path.Combine(temp.Path, "suite-notes.md");

        using var ps = CreateBenchmarkPowerShell($$"""
            $scenarios = @(
                [pscustomobject]@{
                    Suite = 'HeavySaveGate'
                    Name = 'Graph.Full.Save'
                    BenchmarkRole = 'Scoreboard'
                    ComparisonScope = 'SaveCapableProviders'
                    Operations = @('Save')
                    Engines = @('Managed', 'PSResourceGet', 'PowerShellGet')
                    BenchmarkInterpretation = 'Save scoreboard: compare save-capable providers only; ModuleFast has no equivalent save command.'
                },
                [pscustomobject]@{
                    Suite = 'HeavySaveCacheGate'
                    Name = 'Graph.Full.Save.ManagedWarmCache'
                    BenchmarkRole = 'Diagnostic'
                    ComparisonScope = 'ManagedOnlySaveCache'
                    Operations = @('Save')
                    Engines = @('Managed')
                    BenchmarkInterpretation = 'Diagnostic only: managed warm-cache save isolates package cache, extraction, and output materialization cost; do not rank it against providers or install rows.'
                }
            )
            $summaryRows = @(
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'HeavySaveGate'
                    Scenario = 'Graph.Full.Save'
                    Host = 'PowerShell7'
                    Operation = 'Save'
                    FastestEngine = 'Managed'
                    FastestMs = '4815.85'
                    ManagedMs = '4815.85'
                    ManagedRank = '1'
                    ManagedVsFastest = '1x'
                },
                [pscustomobject]@{
                    BenchmarkRole = 'Diagnostic'
                    Suite = 'HeavySaveCacheGate'
                    Scenario = 'Graph.Full.Save.ManagedWarmCache'
                    Host = 'PowerShell7'
                    Operation = 'Save'
                    FastestEngine = 'Managed'
                    FastestMs = '1944.07'
                    ManagedMs = '1944.07'
                    ManagedRank = '1'
                    ManagedVsFastest = '1x'
                }
            )
            $hostRows = @([pscustomobject]@{ Host = 'PowerShell7'; Status = 'Available'; Executable = 'pwsh'; Reason = '' })
            $optimizationRows = @(
                [pscustomobject]@{
                    BenchmarkRole = 'Scoreboard'
                    Suite = 'HeavySaveGate'
                    Scenario = 'Graph.Full.Save'
                    Host = 'PowerShell7'
                    Operation = 'Save'
                    ManagedMs = '4815.85'
                    Bottleneck = 'RootDependency'
                    BottleneckMs = '3966.4'
                    BottleneckShare = '82.4%'
                    NextQuestion = 'Can dependency scheduling, installed-version reuse, or repository lookup fan-out shrink the root operation?'
                    LastMs = '4815.85'
                    LastBottleneck = 'RootDependency'
                    LastBottleneckMs = '3966.4'
                    LastCoalescedWaitMs = '700'
                    LastSlowestMaterializedPackage = 'Microsoft.Graph.Files'
                    LastSlowestMaterializedPackageMs = '450'
                    LastNextQuestion = 'Can dependency scheduling, installed-version reuse, or repository lookup fan-out shrink the root operation?'
                },
                [pscustomobject]@{
                    BenchmarkRole = 'Diagnostic'
                    Suite = 'HeavySaveCacheGate'
                    Scenario = 'Graph.Full.Save.ManagedWarmCache'
                    Host = 'PowerShell7'
                    Operation = 'Save'
                    ManagedMs = '1944.07'
                    Bottleneck = 'Extraction'
                    BottleneckMs = '1100'
                    BottleneckShare = '56.6%'
                    NextQuestion = 'Can archive extraction, path creation, or file writes be reduced safely?'
                    LastMs = '1944.07'
                    LastBottleneck = 'Extraction'
                    LastBottleneckMs = '1100'
                    LastCoalescedWaitMs = '125'
                    LastSlowestMaterializedPackage = 'Microsoft.Graph.Teams'
                    LastSlowestMaterializedPackageMs = '478'
                    LastNextQuestion = 'Can archive extraction, path creation, or file writes be reduced safely?'
                }
            )

            Write-ManagedBenchmarkSuiteNotes -Scenarios $scenarios -SummaryRows $summaryRows -OptimizationRows $optimizationRows -HostRows $hostRows -GateViolations @() -HostGateViolations @() -Path '{{EscapePowerShellString(notesPath)}}' -GeneratedAt ([datetime]'2026-06-29T00:00:00Z')
            """);

        ps.Invoke();

        AssertNoErrors(ps);
        var markdown = File.ReadAllText(notesPath);
        Assert.Contains("`Scoreboard` rows are provider comparisons", markdown, StringComparison.Ordinal);
        Assert.Contains("`Diagnostic` rows are managed-only or managed-focused evidence", markdown, StringComparison.Ordinal);
        Assert.Contains("## Scoreboards", markdown, StringComparison.Ordinal);
        Assert.Contains("Graph.Full.Save", markdown, StringComparison.Ordinal);
        Assert.Contains("Save scoreboard: compare save-capable providers only", markdown, StringComparison.Ordinal);
        Assert.Contains("## Diagnostics", markdown, StringComparison.Ordinal);
        Assert.Contains("Graph.Full.Save.ManagedWarmCache", markdown, StringComparison.Ordinal);
        Assert.Contains("do not rank it against providers or install rows", markdown, StringComparison.Ordinal);
        Assert.Contains("| Diagnostic | HeavySaveCacheGate | Graph.Full.Save.ManagedWarmCache |", markdown, StringComparison.Ordinal);
        Assert.Contains("## Optimization Targets", markdown, StringComparison.Ordinal);
        Assert.Contains("Use these rows to decide where the next managed-engine optimization should start.", markdown, StringComparison.Ordinal);
        Assert.Contains("LastCoalescedWaitMs", markdown, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Graph.Teams", markdown, StringComparison.Ordinal);
        Assert.Contains("Can archive extraction, path creation, or file writes be reduced safely?", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void SuiteNotes_IncludesGateInterpretationWhenDiagnosticRowsFailAGate()
    {
        using var temp = new TemporaryDirectory();
        var notesPath = Path.Combine(temp.Path, "suite-notes.md");

        using var ps = CreateBenchmarkPowerShell($$"""
            $scenario = [pscustomobject]@{
                Suite = 'HeavySaveCacheGate'
                Name = 'Az.Full.Save.ManagedWarmCache'
                BenchmarkRole = 'Diagnostic'
                ComparisonScope = 'ManagedOnlySaveCache'
                Operations = @('Save')
                Engines = @('Managed')
                BenchmarkInterpretation = 'Diagnostic only: managed warm-cache save isolates package cache, extraction, and output materialization cost; do not rank it against providers or install rows.'
            }
            $summaryRow = [pscustomobject]@{
                BenchmarkRole = 'Diagnostic'
                Suite = 'HeavySaveCacheGate'
                Scenario = 'Az.Full.Save.ManagedWarmCache'
                Host = 'PowerShell7'
                Operation = 'Save'
                FastestEngine = 'Managed'
                FastestMs = '5000'
                ManagedMs = '5000'
                ManagedRank = '1'
                ManagedVsFastest = '1x'
            }
            $gateViolation = [pscustomobject]@{
                BenchmarkRole = 'Diagnostic'
                Suite = 'HeavySaveCacheGate'
                Scenario = 'Az.Full.Save.ManagedWarmCache'
                Host = 'PowerShell7'
                Operation = 'Save'
                Reason = 'Managed row exceeded experimental threshold.'
                BenchmarkInterpretation = $scenario.BenchmarkInterpretation
            }

            Write-ManagedBenchmarkSuiteNotes -Scenarios @($scenario) -SummaryRows @($summaryRow) -OptimizationRows @() -HostRows @() -GateViolations @($gateViolation) -HostGateViolations @() -Path '{{EscapePowerShellString(notesPath)}}' -GeneratedAt ([datetime]'2026-06-29T00:00:00Z')
            """);

        ps.Invoke();

        AssertNoErrors(ps);
        var markdown = File.ReadAllText(notesPath);
        Assert.Contains("## Performance Gate Violations", markdown, StringComparison.Ordinal);
        Assert.Contains("Managed row exceeded experimental threshold.", markdown, StringComparison.Ordinal);
        Assert.Contains("do not rank it against providers or install rows", markdown, StringComparison.Ordinal);
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var notesScript = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.SuiteNotes.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(File.ReadAllText(notesScript) + Environment.NewLine + script);
        return ps;
    }

    private static string EscapePowerShellString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString()));
        Assert.Fail(message);
    }
}
