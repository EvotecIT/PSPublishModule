using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedStableVectorCapturesPreservePowerShellOutput(string framework, string host)
    {
        var encryption = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "ActiveDirectory", "Get-ADEncryptionTypes.ps1");
        var trust = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "ActiveDirectory", "Get-ADTrustAttributes.ps1");
        using var fixture = ArtifactFixture.Create(
            File.ReadAllText(encryption) + Environment.NewLine + File.ReadAllText(trust) + Environment.NewLine +
            "Export-ModuleMember -Function Get-ADEncryptionTypes,Get-ADTrustAttributes" + Environment.NewLine,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.StableVectorCaptures",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.PromotedTypedRegions);

        const string probe = """
            function Describe-Vector($command, $case, $arguments) {
                $records = @(& $command @arguments)
                [pscustomobject]@{
                    command = $command
                    case = $case
                    count = $records.Count
                    values = @($records | ForEach-Object { [string]$_ })
                    types = @($records | ForEach-Object { $_.GetType().FullName })
                } | ConvertTo-Json -Depth 5 -Compress
            }
            foreach ($name in 'Get-ADEncryptionTypes','Get-ADTrustAttributes') {
                Describe-Vector $name 'default' @{}
                Describe-Vector $name 'zero' @{ Value = 0 }
                Describe-Vector $name 'one' @{ Value = 1 }
                Describe-Vector $name 'many' @{ Value = 223 }
                Describe-Vector $name 'unknown-bit' @{ Value = 256 }
                $records = @(1,24 | & $name)
                'pipeline:' + $name + ':' + ($records -join '|')
            }
            $first = @(& { foreach ($value in 1..200) { Get-ADEncryptionTypes -Value 24 } } | Select-Object -First 1)
            'stopped=' + ($first -join '|')
            'reuse=' + (@(Get-ADEncryptionTypes -Value 24) -join '|')
            Remove-Module $module.Name
            $module = Import-Module $modulePath -PassThru
            'reimport=' + (@(Get-ADTrustAttributes -Value 5) -join '|')
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath,
            "stable-vector-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath,
            "stable-vector-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("reuse=AES128-CTS-HMAC-SHA1-96|AES256-CTS-HMAC-SHA1-96", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_PinnedStableVectorCapturesPromoteGuardedPrefixes()
    {
        var cases = new[]
        {
            (File: "Get-ADEncryptionTypes.ps1", Hash: "b650fb6421a24279c594182467cacb2833c3fa1b8a31edae8d240b5edbe88b0f", Target: "EncryptionTypes"),
            (File: "Get-ADTrustAttributes.ps1", Hash: "bc51e28a0b080b6ed562c2edc49c8a1a5362612ccfe6c9b47ddc099ec9ccd416", Target: "TrustAttributes")
        };

        foreach (var item in cases)
        {
            var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "ActiveDirectory", item.File);
            Assert.Equal(item.Hash,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
            var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
                new[] { source }, "PowerForge.Compiled", Path.GetFileNameWithoutExtension(item.File) + "Methods", "net10.0",
                PowerShellCompilationCapabilities.HybridModule);
            var regions = typed.PromotedRegions.Where(candidate =>
                    candidate.SourceName.Equals(Path.GetFileNameWithoutExtension(item.File), StringComparison.OrdinalIgnoreCase))
                .OrderBy(static candidate => candidate.StartOffset).ToArray();
            Assert.True(regions.Length == 2,
                string.Join(Environment.NewLine, typed.RegionCandidates.Select(candidate =>
                    candidate.StartLine + "-" + candidate.EndLine + " " + candidate.DecisionCode + ": " + candidate.Reason)) +
                Environment.NewLine + string.Join(Environment.NewLine, typed.RegionOpportunities.Select(opportunity =>
                    opportunity.StartLine + "-" + opportunity.EndLine + " " + opportunity.Continuation +
                    " inputs=" + string.Join(",", opportunity.LiveInputs.Select(input => input.Identity + ":" + input.TypeName)) +
                    " outputs=" + string.Join(",", opportunity.LiveOutputs.Select(output => output.Identity + ":" + output.TypeName)))));
            var region = regions[0];
            Assert.True(region.RequiresLocalOwnershipGuard,
                $"Promoted {region.StartLine}-{region.EndLine}; continuation=" +
                string.Join(",", region.ContinuationLocals.Select(static local => local.Name)) + Environment.NewLine +
                string.Join(Environment.NewLine, typed.RegionCandidates.Select(candidate =>
                    candidate.StartLine + "-" + candidate.EndLine + " " + candidate.DecisionCode + ": " + candidate.Reason)));
            Assert.Equal(new[] { item.Target, "V" }, region.ContinuationLocals.Select(static local => local.Name));
            Assert.All(region.ContinuationLocals, static local =>
            {
                var contract = Assert.IsType<PowerShellRegionTransferContract>(local.Contract);
                Assert.Equal(PowerShellRegionTransferDirection.LiveOut, contract.Direction);
                Assert.Equal(PowerShellRegionTransferOwnership.GuardedFresh, contract.Ownership);
                Assert.Equal(PowerShellRegionTransferMutation.RetainedOnly, contract.Mutation);
            });
            var vector = Assert.IsType<PowerShellRegionTransferContract>(region.ContinuationLocals[0].Contract);
            Assert.Equal(PowerShellRegionTransferShape.StableScalarVector, vector.Shape);
            Assert.Empty(region.RegionGraph.Regions.SelectMany(static graph => graph.Streams));
            Assert.Empty(regions[1].ContinuationLocals);
            Assert.Equal(PowerShellRegionTransferOutputBehavior.EnumerateOneLevel,
                Assert.IsType<PowerShellRegionTransferContract>(regions[1].TerminalTransferContract).OutputBehavior);
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_StableVectorCaptureRejectsOpenOutputAndEnumerationContracts()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ObjectVector { param([int]$Value) [object[]]$Items = @(foreach ($item in $Value) { $item }); $Items }
            function Get-ArbitraryEnumerable { param([System.Collections.IEnumerable]$Value) [string[]]$Items = @(foreach ($item in $Value) { [string]$item }); $Items }
            function Get-NullableScalar { param([Nullable[int]]$Value) [string[]]$Items = @(foreach ($item in $Value) { [string]$item }); $Items }
            function Get-ReferenceScalar { param([uri]$Value) [string[]]$Items = @(foreach ($item in $Value) { [string]$item }); $Items }
            function Get-ShapedOutput { param([int]$Value) [string[]]$Items = @(foreach ($item in $Value) { Write-Output -NoEnumerate ([string]$item) }); $Items }
            function Get-EscapingOutput { param([int]$Value) [string[]]$Items = @(foreach ($item in $Value) { return ([string]$item) }); $Items }
            Export-ModuleMember -Function Get-*
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "RejectedStableVectorCaptures", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.DoesNotContain(typed.PromotedRegions,
            static region => region.ContinuationLocals.Count > 0);
        Assert.All(new[]
            {
                "Get-ObjectVector", "Get-ArbitraryEnumerable", "Get-NullableScalar", "Get-ReferenceScalar",
                "Get-ShapedOutput", "Get-EscapingOutput"
            },
            name => Assert.DoesNotContain(typed.PromotedRegions,
                region => region.SourceName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                          region.ContinuationLocals.Count > 0));
    }
}
