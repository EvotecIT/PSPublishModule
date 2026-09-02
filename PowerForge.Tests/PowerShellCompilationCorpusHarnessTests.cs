using System.Management.Automation.Language;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void PublicCorpusBaselineReportsZeroSuccessWithoutPropertyEnumerationFailure()
    {
        var scriptPath = Path.Combine(
            FindCorpusRepositoryRoot(),
            "Benchmarks",
            "PowerShellCompilation",
            "Corpus",
            "Invoke-PublicCorpus.ps1");
        var ast = Parser.ParseFile(scriptPath, out _, out var parseErrors);
        Assert.Empty(parseErrors);
        var comparisonFunction = Assert.IsType<FunctionDefinitionAst>(ast.Find(
            static node => node is FunctionDefinitionAst function &&
                           function.Name.Equals("Compare-PublicBaseline", StringComparison.Ordinal),
            searchNestedScriptBlocks: true));
        var command = $$"""
            Set-StrictMode -Version Latest
            function Get-Sum { param([int[]] $Values) return [int] (($Values | Measure-Object -Sum).Sum) }
            {{comparisonFunction.Extent.Text}}
            $baseline = [pscustomobject]@{
                schemaVersion = 1
                packetId = 'generic-public-corpus'
                packetSha256 = 'packet-hash'
                semanticProfile = 'profile'
                strict = [pscustomobject]@{
                    programs = 4
                    analyzedUnits = 4
                    boundUnits = 4
                    emittedClrUnits = 4
                    targetHosts = @([pscustomobject]@{
                        runtimeIdentifier = 'win-x64'
                        programsPassed = 4
                        programsTotal = 4
                        qualifiedApplications = @()
                    })
                }
            }
            $packet = [pscustomobject]@{ packetId = 'generic-public-corpus'; semanticProfile = 'profile' }
            $failed = @(
                [pscustomobject]@{ id = 'one'; succeeded = $false },
                [pscustomobject]@{ id = 'two'; succeeded = $false },
                [pscustomobject]@{ id = 'three'; succeeded = $false },
                [pscustomobject]@{ id = 'four'; succeeded = $false }
            )
            $regressions = @(Compare-PublicBaseline -Baseline $baseline -Packet $packet -PacketSha256 'packet-hash' -Modules @() -StrictPrograms $failed -Rid 'win-x64' -CompareModules $false -CompareStrict $true)
            [Console]::Out.Write($regressions.Count)
            """;

        var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("4", result.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
    }

    private static string FindCorpusRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Benchmarks", "PowerShellCompilation", "Corpus", "Invoke-PublicCorpus.ps1")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate the PowerShell compilation corpus runner.");
    }
}
