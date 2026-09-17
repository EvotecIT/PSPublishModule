using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_NoValueAndNullRegionsPreserveCardinalityAndContinuation(
        string framework,
        string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ConditionalNoValue {
                param([bool]$Stop)
                'head'
                if ($Stop) { return }
                Write-Error 'continued-error' -ErrorAction Continue
                'tail'
            }
            function Get-ConditionalNull {
                param([bool]$Stop)
                'head'
                if ($Stop) { return $null }
                Write-Error 'continued-error' -ErrorAction Continue
                'tail'
            }
            function Get-TerminalNoValue {
                Write-Error 'terminal-error' -ErrorAction Continue
                data CardinalityBarrier { }
                return
            }
            function Get-TerminalNull {
                Write-Error 'terminal-error' -ErrorAction Continue
                data CardinalityBarrier { }
                return $null
            }
            function Get-DirectNull {
                return $null
            }
            Export-ModuleMember -Function Get-ConditionalNoValue,Get-ConditionalNull,Get-TerminalNoValue,Get-TerminalNull,Get-DirectNull
            """, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ValueCardinalityRegions", framework,
            PowerShellCompilationCapabilities.HybridModule);
        var regions = typed.PromotedRegions
            .Where(static region =>
                region.TerminalTransferContract?.Shape is PowerShellRegionTransferShape.NoValue or PowerShellRegionTransferShape.NullValue ||
                region.ControlFlowContract?.ReturnValue.Shape is PowerShellRegionTransferShape.NoValue or PowerShellRegionTransferShape.NullValue)
            .OrderBy(static region => region.SourceName, StringComparer.Ordinal)
            .ToArray();

        Assert.True(regions.Length == 4,
            string.Join(Environment.NewLine, typed.RegionCandidates.Select(static candidate =>
                candidate.SourceName + " " + candidate.StartLine + "-" + candidate.EndLine + " " +
                candidate.DecisionCode + ": " + candidate.Reason + " terminal=" +
                candidate.TerminalTransferContract?.Shape + " control=" +
                candidate.ControlFlowContract?.ReturnValue.Shape)));
        AssertCardinalityRegion(regions, "Get-ConditionalNoValue", PowerShellRegionTransferShape.NoValue,
            PowerShellRegionTransferOutputBehavior.None, controlFlow: true);
        AssertCardinalityRegion(regions, "Get-ConditionalNull", PowerShellRegionTransferShape.NullValue,
            PowerShellRegionTransferOutputBehavior.Atomic, controlFlow: true);
        AssertCardinalityRegion(regions, "Get-TerminalNoValue", PowerShellRegionTransferShape.NoValue,
            PowerShellRegionTransferOutputBehavior.None, controlFlow: false);
        AssertCardinalityRegion(regions, "Get-TerminalNull", PowerShellRegionTransferShape.NullValue,
            PowerShellRegionTransferOutputBehavior.Atomic, controlFlow: false);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.ValueCardinalityRegions",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(typed.PromotedRegions.Count(), result.Manifest!.PromotedTypedRegions);

        const string probe = """
            function Describe-Invocation([string]$Name, [hashtable]$Arguments) {
                $records = @(& $Name @Arguments 2>&1)
                [pscustomobject]@{
                    name = $Name
                    arguments = ($Arguments.GetEnumerator() | Sort-Object Key | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ','
                    count = $records.Count
                    types = @($records | ForEach-Object { if ($null -eq $_) { '<null>' } else { $_.GetType().FullName } })
                    values = @($records | ForEach-Object { [string]$_ })
                } | ConvertTo-Json -Depth 5 -Compress
            }
            foreach ($pass in 1..2) {
                Describe-Invocation 'Get-ConditionalNoValue' @{ Stop = $true }
                Describe-Invocation 'Get-ConditionalNoValue' @{ Stop = $false }
                Describe-Invocation 'Get-ConditionalNull' @{ Stop = $true }
                Describe-Invocation 'Get-ConditionalNull' @{ Stop = $false }
                Describe-Invocation 'Get-TerminalNoValue' @{}
                Describe-Invocation 'Get-TerminalNull' @{}
                Describe-Invocation 'Get-DirectNull' @{}
                Get-ConditionalNull -Stop:$true | Select-Object -First 1 | Out-Null
            }
            """;
        var original = RunStatementErrorProbe(host,
            "$module = Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "' -PassThru; " + probe +
            "; Remove-Module $module.Name -Force; " +
            "$module = Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "' -PassThru -Force; " + probe,
            fixture.RootPath,
            "value-cardinality-original");
        var compiled = RunStatementErrorProbe(host,
            "$module = Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "' -PassThru; " + probe +
            "; Remove-Module $module.Name -Force; " +
            "$module = Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "' -PassThru -Force; " + probe,
            fixture.RootPath,
            "value-cardinality-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("param([bool]$Stop) if ($Stop) { return [System.Management.Automation.Internal.AutomationNull]::Value }; data Barrier { }; 'continued'")]
    [InlineData("param([bool]$Stop) if ($Stop) { return $(if ($false) { 1 }) }; data Barrier { }; 'continued'")]
    [InlineData("param([bool]$Stop,[object]$Value) if ($Stop) { return $Value }; data Barrier { }; 'continued'")]
    public void Transpile_ValueCardinalityRegionsKeepDynamicAndObjectReturnsRetained(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-UnprovedCardinality { " + body + " }" + Environment.NewLine,
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "UnprovedValueCardinality", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.DoesNotContain(typed.PromotedRegions,
            static region => region.SourceName == "Get-UnprovedCardinality" && region.ControlFlowContract is not null);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_PinnedImmutableIdPromotesTerminalNoValueReturn()
    {
        var source = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "Converts", "ConvertTo-ImmutableID.ps1");
        Assert.Equal(
            "0931d254c96baa69b17cdf19d2ef5d5c30c3c6ce3b49c8bcc62a373570cf3d75",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { source }, "PowerForge.Compiled", "PinnedImmutableId", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        var region = Assert.Single(typed.PromotedRegions,
            static item => item.SourceName == "ConvertTo-ImmutableID" &&
                           item.TerminalTransferContract?.Shape == PowerShellRegionTransferShape.NoValue);
        Assert.Equal(43, region.StartLine);
        Assert.Equal(PowerShellRegionTransferElementContract.None, region.TerminalTransferContract!.ElementContract);
        Assert.Equal(PowerShellRegionTransferOutputBehavior.None, region.TerminalTransferContract.OutputBehavior);
        Assert.Empty(region.RegionGraph.Regions.Single().Streams);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedExplicitNullReturnsRemainOneRecord(string framework, string host)
    {
        var pastWeek = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Private", "Deprecated", "Dates", "Find-DatesPastWeek.ps1");
        var cloudCache = FindCompleteConversionWorkflow(
            "CleanupMonster", "FullModule", "Private", "Get-CloudCacheComputer.ps1");
        Assert.Equal(
            "29636d7424d613840de0df92fa2ff766614bb1d14152d81d22ace6e449e3ef59",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pastWeek))).ToLowerInvariant());
        Assert.Equal(
            "968d87fb13baaeef9796930e515f397f62f0266fc0a2a2bae9cd884637832f99",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(cloudCache))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(
            File.ReadAllText(pastWeek) + Environment.NewLine +
            File.ReadAllText(cloudCache) + Environment.NewLine +
            "Export-ModuleMember -Function Find-DatesPastWeek,Get-CloudCacheComputer" + Environment.NewLine,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.PinnedNullReturns",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        const string probe = """
            $cases = @(
                @{ Name = 'past-week-null'; Records = @(Find-DatesPastWeek -DayName '__never__') },
                @{ Name = 'cloud-cache-null'; Records = @(Get-CloudCacheComputer -Cache $null -Candidates @('A')) },
                @{ Name = 'cloud-cache-value'; Records = @(Get-CloudCacheComputer -Cache ([ordered]@{ A = 'value' }) -Candidates @('missing','A')) }
            )
            foreach ($case in $cases) {
                [pscustomobject]@{
                    name = $case.Name
                    count = $case.Records.Count
                    types = @($case.Records | ForEach-Object { if ($null -eq $_) { '<null>' } else { $_.GetType().FullName } })
                    values = @($case.Records | ForEach-Object { [string]$_ })
                } | ConvertTo-Json -Depth 5 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath,
            "pinned-null-returns-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath,
            "pinned-null-returns-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"name\":\"past-week-null\",\"count\":1", original.StandardOutput,
            StringComparison.Ordinal);
        Assert.Contains("\"name\":\"cloud-cache-null\",\"count\":1", original.StandardOutput,
            StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    private static void AssertCardinalityRegion(
        PowerShellCompiledRegion[] regions,
        string sourceName,
        PowerShellRegionTransferShape shape,
        PowerShellRegionTransferOutputBehavior output,
        bool controlFlow)
    {
        var region = Assert.Single(regions, item => item.SourceName == sourceName);
        var contract = controlFlow
            ? Assert.IsType<PowerShellRegionControlFlowContract>(region.ControlFlowContract).ReturnValue
            : Assert.IsType<PowerShellRegionTransferContract>(region.TerminalTransferContract);
        Assert.Equal(shape, contract.Shape);
        Assert.Equal(PowerShellRegionTransferElementContract.None, contract.ElementContract);
        Assert.Equal(PowerShellRegionTransferDirection.TerminalSuccess, contract.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.Unspecified, contract.Ownership);
        Assert.Equal(output, contract.OutputBehavior);
        Assert.Equal(PowerShellRegionTransferMutation.None, contract.Mutation);
        Assert.True(contract.Supported);
        Assert.Empty(region.ContinuationLocals);
        var streams = region.RegionGraph.Regions.Single().Streams;
        if (!controlFlow && output == PowerShellRegionTransferOutputBehavior.Atomic)
            Assert.Equal(new[] { "Success" }, streams);
        else
            Assert.Empty(streams);
    }
}
