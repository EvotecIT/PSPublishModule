namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedFirewallAndTimeInFullModule(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("", ".psm1");
        CopyPinnedWorkflowModule(fixture, "PSSharedGoods", "87f61869110c6e6950603027fe4c9304d4d7a4386008754e009710cabae37ea6", 285);
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        Assert.Equal(284, resolved.SourceFiles.Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath, fixture.OutputPath, "Generated.OfflineProviderCoverage", resolved.Kind,
            resolved.Mode, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework,
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            RuntimeSourcePaths = resolved.SourceFiles,
            ModuleManifestPath = resolved.ModuleManifestPath
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in new[] { "Get-ComputerFirewall", "Get-ComputerTime" })
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == name);
            Assert.True(unit.EmittedClrMethod, name + ": " + string.Join("; ", unit.DiagnosticChain.Select(cause => cause.Message)));
            Assert.False(unit.RetainedHostedSource);
        }

        var observerPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PowerShellCompilationInventoryWorkflow", "OfflineProviderCoverage.ps1");
        var observer = File.ReadAllText(observerPath);
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + observer,
            fixture.RootPath, "offline-provider-original");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + observer,
            fixture.RootPath, "offline-provider-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.Equal(original.StandardError, compiled.StandardError);
        var expected = original.StandardOutput.Split('\n').Where(line => line.StartsWith("{", StringComparison.Ordinal)).ToArray();
        var actual = compiled.StandardOutput.Split('\n').Where(line => line.StartsWith("{", StringComparison.Ordinal)).ToArray();
        Assert.Equal(7, expected.Length);
        var observations = expected.Select(line => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(line)).ToArray();
        Assert.Equal("firewall", observations[0].GetProperty("step").GetString());
        Assert.Equal(1, observations[0].GetProperty("count").GetInt32());
        Assert.Equal("rule-a", observations[0].GetProperty("values")[0].GetProperty("name").GetString());
        Assert.Contains(observations[0].GetProperty("calls").EnumerateArray(), call => call.GetString() == "rule");
        Assert.Equal("firewall-repeat", observations[1].GetProperty("step").GetString());
        Assert.Equal(1, observations[1].GetProperty("count").GetInt32());
        Assert.Equal("firewall-empty", observations[2].GetProperty("step").GetString());
        Assert.Equal(0, observations[2].GetProperty("count").GetInt32());
        Assert.Equal("time", observations[3].GetProperty("step").GetString());
        Assert.Equal(2, observations[3].GetProperty("count").GetInt32());
        Assert.Contains(observations[3].GetProperty("calls").EnumerateArray(), call => call.GetString() == "cim:Win32_LocalTime");
        Assert.Equal("time-repeat", observations[4].GetProperty("step").GetString());
        Assert.Equal(2, observations[4].GetProperty("count").GetInt32());
        Assert.Equal("time-missing", observations[5].GetProperty("step").GetString());
        Assert.Equal("Unable to get time difference.", observations[5].GetProperty("values")[1].GetProperty("status").GetString());
        Assert.Equal("cleanup", observations[6].GetProperty("step").GetString());
        Assert.Equal(0, observations[6].GetProperty("remaining").GetInt32());
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(4).Select(pair =>
                "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
    }
}
