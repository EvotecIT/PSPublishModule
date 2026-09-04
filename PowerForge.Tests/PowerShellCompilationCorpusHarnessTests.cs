using System.Management.Automation.Language;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void PublicCorpusBaselineDerivedHybridPercentageIsConsistent()
    {
        var baselinePath = Path.Combine(
            FindCorpusRepositoryRoot(),
            "Benchmarks",
            "PowerShellCompilation",
            "Corpus",
            "public-corpus-baseline.net8.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(baselinePath));
        var hybrid = document.RootElement.GetProperty("hybrid");
        var analyzed = hybrid.GetProperty("analyzedUnits").GetInt32();
        var emitted = hybrid.GetProperty("emittedClrUnits").GetInt32();
        var recorded = hybrid.GetProperty("emittedClrUnitPercentage").GetDouble();
        var derived = analyzed == 0 ? 0.0 : Math.Round(100.0 * emitted / analyzed, 2);

        Assert.Equal(derived, recorded, precision: 2);
    }

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

    [Fact]
    public void CorpusRegionCandidateProjectionIsPortableAndHandlesPreLoweringRejection()
    {
        var repositoryRoot = FindCorpusRepositoryRoot();
        var commonScript = Path.Combine(
            repositoryRoot,
            "Benchmarks",
            "PowerShellCompilation",
            "Corpus",
            "Corpus.Runner.Common.ps1");
        var sourceRoot = Path.Combine(Path.GetTempPath(), "PowerForge Candidate Projection", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(sourceRoot, "Nested", "Module.psm1");
        var candidateJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            regionId = "region:test",
            sourceSha256 = new string('a', 64),
            sourceDocumentSha256 = new string('b', 64),
            sourceName = "Get-Proof",
            sourceLine = 4,
            sourcePath,
            startOffset = 10,
            endOffset = 25,
            startLine = 8,
            startColumn = 2,
            endLine = 8,
            endColumn = 15,
            promoted = false,
            decisionCode = "PSD1001",
            reason = "A local value is not definitely assigned.",
            generatedName = string.Empty
        });
        var command = $$"""
            . '{{commonScript.Replace("'", "''", StringComparison.Ordinal)}}'
            $candidate = @'
            {{candidateJson}}
            '@ | ConvertFrom-Json
            $portable = ConvertTo-PortableRegionCandidate -Candidate $candidate -SourceRoot '{{sourceRoot.Replace("'", "''", StringComparison.Ordinal)}}'
            $graphCandidate = $candidate | Select-Object *
            $graphCandidate.decisionCode = 'region.command-boundary'
            $graphCandidate | Add-Member -NotePropertyName regionGraph -NotePropertyValue ([pscustomobject]@{
                schemaVersion = 1
                hostedCommandBoundarySites = 1
                moduleStateReadBoundarySites = 0
                moduleStateWriteBoundarySites = 0
                staticBoundaryCrossings = 1
                staticBoundaryCostUnits = 3
                regions = @([pscustomobject]@{
                    regionId = 'region:test:0'; ordinal = 0; execution = 'Mixed'
                    startOffset = 10; endOffset = 25; startLine = 8; startColumn = 2; endLine = 8; endColumn = 15
                    inputs = @('Local:VALUE'); outputs = @('Success'); mutations = @(); streams = @('Success'); errors = @()
                    ordering = 'Authored'; hostedCommandBoundarySites = 1; moduleStateReadBoundarySites = 0; moduleStateWriteBoundarySites = 0
                    staticBoundaryCrossings = 1; staticBoundaryValueTransfers = 2; staticBoundaryCostUnits = 3
                })
            })
            $portableGraph = ConvertTo-PortableRegionCandidate -Candidate $graphCandidate -SourceRoot '{{sourceRoot.Replace("'", "''", StringComparison.Ordinal)}}'
            $frontier = @(Get-CrossWorkloadRetainedRegionFrontier -Rows @(
                [pscustomobject]@{ WorkloadId = 'one'; ScenarioFamily = 'family-one'; Candidate = $portable },
                [pscustomobject]@{ WorkloadId = 'two'; ScenarioFamily = 'family-two'; Candidate = $portable }
            ))
            [pscustomobject]@{
                candidate = $portable
                graphCandidate = $portableGraph
                summary = @(Get-RegionCandidateDecisionSummary -Candidates @($portable))
                frontier = $frontier
            } | ConvertTo-Json -Compress -Depth 20
            """;

        var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        using var document = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("Nested/Module.psm1", root.GetProperty("candidate").GetProperty("relativePath").GetString());
        Assert.Equal(10, root.GetProperty("candidate").GetProperty("startOffset").GetInt32());
        Assert.Equal(25, root.GetProperty("candidate").GetProperty("endOffset").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("candidate").GetProperty("graph").ValueKind);
        Assert.Equal(1, root.GetProperty("graphCandidate").GetProperty("graph").GetProperty("regionCount").GetInt32());
        Assert.Equal(2, root.GetProperty("graphCandidate").GetProperty("graph").GetProperty("staticBoundaryValueTransfers").GetInt32());
        Assert.Equal("Mixed", root.GetProperty("graphCandidate").GetProperty("graph").GetProperty("regions")[0].GetProperty("execution").GetString());
        Assert.Equal("PSD1001", root.GetProperty("summary")[0].GetProperty("decisionCode").GetString());
        Assert.Equal(1, root.GetProperty("summary")[0].GetProperty("candidates").GetInt32());
        Assert.Equal(2, root.GetProperty("frontier")[0].GetProperty("affectedScenarioFamilies").GetInt32());
        Assert.Equal(2, root.GetProperty("frontier")[0].GetProperty("affectedWorkloads").GetInt32());
    }

    [Fact]
    public void CorpusRegionOpportunityProjectionIsPortableAnalysisOnlyEvidence()
    {
        var repositoryRoot = FindCorpusRepositoryRoot();
        var commonScript = Path.Combine(
            repositoryRoot,
            "Benchmarks",
            "PowerShellCompilation",
            "Corpus",
            "Corpus.Runner.Common.ps1");
        var sourceRoot = Path.Combine(Path.GetTempPath(), "PowerForge Opportunity Projection", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(sourceRoot, "Nested", "Module.psm1");
        var opportunityJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            opportunityId = "opportunity:test",
            sourceSha256 = new string('a', 64),
            sourceDocumentSha256 = new string('b', 64),
            sourceName = "Get-Proof",
            sourceLine = 4,
            sourcePath,
            startOffset = 10,
            endOffset = 40,
            startLine = 8,
            startColumn = 2,
            endLine = 10,
            endColumn = 8,
            startStatementIndex = 1,
            endStatementIndex = 3,
            statementCount = 3,
            continuation = "UnboundFallThrough",
            continuationAnalysisComplete = false,
            liveInputSourceAnalysisComplete = true,
            liveOutputConsumerAnalysisComplete = false,
            insideTerminalCandidate = false,
            liveInputs = new[] { new { identity = "Parameter:VALUE", typeName = "System.Int32", typeProvenance = "Explicit", stableScalar = true } },
            liveOutputs = new[] { new { identity = "Local:RESULT", typeName = "System.Int32", typeProvenance = "Explicit", stableScalar = true } },
            localCalls = Array.Empty<string>(),
            regionGraph = new
            {
                schemaVersion = 1,
                hostedCommandBoundarySites = 0,
                moduleStateReadBoundarySites = 0,
                moduleStateWriteBoundarySites = 0,
                staticBoundaryCrossings = 0,
                staticBoundaryCostUnits = 0,
                regions = new[]
                {
                    new
                    {
                        regionId = "region:test:0", ordinal = 0, execution = "Typed",
                        startOffset = 10, endOffset = 40, startLine = 8, startColumn = 2, endLine = 10, endColumn = 8,
                        inputs = new[] { "Parameter:VALUE" }, outputs = new[] { "Local:RESULT" }, mutations = new[] { "Local:RESULT" },
                        streams = Array.Empty<string>(), errors = Array.Empty<string>(), ordering = "AuthoredSequentialSingleEvaluation",
                        hostedCommandBoundarySites = 0, moduleStateReadBoundarySites = 0, moduleStateWriteBoundarySites = 0,
                        staticBoundaryCrossings = 0, staticBoundaryValueTransfers = 0, staticBoundaryCostUnits = 0
                    }
                }
            },
            analysisOnly = true
        });
        var command = $$"""
            Set-StrictMode -Version 3.0
            . '{{commonScript.Replace("'", "''", StringComparison.Ordinal)}}'
            $opportunity = @'
            {{opportunityJson}}
            '@ | ConvertFrom-Json
            $portable = ConvertTo-PortableRegionOpportunity -Opportunity $opportunity -SourceRoot '{{sourceRoot.Replace("'", "''", StringComparison.Ordinal)}}'
            $frontier = @(Get-CrossWorkloadRegionOpportunityFrontier -Rows @(
                [pscustomobject]@{ WorkloadId = 'one'; ScenarioFamily = 'family-one'; Opportunity = $portable },
                [pscustomobject]@{ WorkloadId = 'two'; ScenarioFamily = 'family-two'; Opportunity = $portable }
            ))
            $invalid = $opportunity | Select-Object *
            $invalid.analysisOnly = $false
            $invalidRejected = $false
            try { ConvertTo-PortableRegionOpportunity -Opportunity $invalid -SourceRoot '{{sourceRoot.Replace("'", "''", StringComparison.Ordinal)}}' | Out-Null }
            catch {
                if (-not $_.Exception.Message.Contains('not marked analysis-only', [StringComparison]::Ordinal)) { throw }
                $invalidRejected = $true
            }
            [pscustomobject]@{ opportunity = $portable; frontier = $frontier; invalidRejected = $invalidRejected } |
                ConvertTo-Json -Compress -Depth 20
            """;

        var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        using var document = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("Nested/Module.psm1", root.GetProperty("opportunity").GetProperty("relativePath").GetString());
        Assert.Equal(3, root.GetProperty("opportunity").GetProperty("statementCount").GetInt32());
        Assert.True(root.GetProperty("opportunity").GetProperty("analysisOnly").GetBoolean());
        Assert.Equal("Local:RESULT", root.GetProperty("opportunity").GetProperty("liveOutputs")[0].GetProperty("identity").GetString());
        Assert.Equal(2, root.GetProperty("frontier")[0].GetProperty("affectedScenarioFamilies").GetInt32());
        Assert.True(root.GetProperty("invalidRejected").GetBoolean());
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
