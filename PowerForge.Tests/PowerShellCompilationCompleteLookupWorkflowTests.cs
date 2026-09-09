namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedLookupCompilesClosureAndPreservesOrder(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("CleanupMonster", "Get-ComputerLookupCandidates.ps1");
        Assert.Equal("d7decce312bf7c17965ac2c018604a2c6c465c268296e9f9ea6bd283b9e98274",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CompleteLookup", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == "Get-ComputerLookupCandidates");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.False(unit.RetainedHostedSource);
        var graph = Assert.IsType<PowerShellCompilationRegionGraph>(unit.RegionGraph);
        var block = Assert.Single(graph.ScriptBlocks);
        Assert.Equal(1, block.Graph.HostedCommandBoundarySites);
        Assert.Equal(6, graph.HostedCommandBoundarySites);
        Assert.Equal(6, unit.RuntimeCommandRegions);
        const string probe = """
            $cases=@(
                @{}, @{Name=$null;DNSHostName=$null;SamAccountName=$null},
                @{Name='';DNSHostName=' ';SamAccountName="`t"},
                @{Name='host';DNSHostName='HOST.example.test';SamAccountName='host$'},
                @{Name=' HOST ';DNSHostName='host';SamAccountName='HOST$'},
                @{Name='first';DNSHostName='second.example.test';SamAccountName='third$$'},
                @{Name='東京';DNSHostName='東京.example.test';SamAccountName='東京$'},
                @{DNSHostName='.example.test';SamAccountName='$'},
                @{DNSHostName='host.'}, @{SamAccountName=' host$ '})
            for ($repeat=0; $repeat -lt 2; $repeat++) {
                for ($index=0; $index -lt $cases.Count; $index++) {
                    $parameters=$cases[$index]; $faults=@(); $Error.Clear()
                    $items=@(Get-ComputerLookupCandidates @parameters -ErrorVariable faults)
                    [pscustomobject]@{case=$index;repeat=$repeat;items=$items;
                        types=@($items | ForEach-Object { $_.GetType().FullName });
                        faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId });errors=$Error.Count} |
                        ConvertTo-Json -Depth 6 -Compress
                }
            }
            'later:' + ((Get-ComputerLookupCandidates -Name 'fresh') -join ',')
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "complete-lookup");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "complete-lookup");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("later:fresh", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
