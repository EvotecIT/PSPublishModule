namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedInventoryInFullModule(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("", ".psm1");
        CopyPinnedWorkflowModule(fixture, "PSSharedGoods", "87f61869110c6e6950603027fe4c9304d4d7a4386008754e009710cabae37ea6", 285);
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        Assert.Equal(284, resolved.SourceFiles.Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath, fixture.OutputPath, "Generated.CompleteInventory", resolved.Kind,
            resolved.Mode, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework,
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            RuntimeSourcePaths = resolved.SourceFiles,
            ModuleManifestPath = resolved.ModuleManifestPath
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var units = result.Manifest!.UnitDispositionLedger!.Entries;
        foreach (var name in new[] { "Get-ComputerDisk", "Get-ComputerWindowsFeatures", "Get-ComputerSMBShareList",
                     "Get-ComputerCPU", "Get-ComputerDevice", "Get-ComputerRAM", "Get-ComputerStartup" })
        {
            var unit = Assert.Single(units, item => item.Name == name);
            Assert.True(unit.EmittedClrMethod, name + ": " + string.Join("; ", unit.DiagnosticChain.Select(cause => cause.Message)));
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        }
        foreach (var name in new[] { "Get-CimData", "Get-ComputerSMBInfo" })
            Assert.True(Assert.Single(units, item => item.Name == name).RetainedHostedSource);
        var observerRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PowerShellCompilationInventoryWorkflow");
        var setup = File.ReadAllText(Path.Combine(observerRoot, "Providers.ps1"));
        var observer = setup + Environment.NewLine + File.ReadAllText(Path.Combine(observerRoot, "Observe.ps1"));
        var original = RunStatementErrorProbe(host, "$module=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + observer, fixture.RootPath, "inventory-original");
        var compiled = RunStatementErrorProbe(host, "$module=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + observer, fixture.RootPath, "inventory-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n').Where(line => line.StartsWith("{", StringComparison.Ordinal)).ToArray();
        var actual = compiled.StandardOutput.Split('\n').Where(line => line.StartsWith("{", StringComparison.Ordinal)).ToArray();
        Assert.Equal(315, expected.Length);
        foreach (var command in new[] { "disk", "features", "shares" })
        {
            var observations = expected.Select(line => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(line));
            var sample = Assert.Single(observations, item => item.GetProperty("command").GetString() == command &&
                item.GetProperty("count").GetInt32() == 1 && item.GetProperty("fault").GetString() == "none" &&
                item.GetProperty("action").GetString() == "Continue" && item.GetProperty("variant").GetInt32() == 0 &&
                item.GetProperty("repeat").GetInt32() == 0);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, sample.GetProperty("caught").ValueKind);
            Assert.Empty(sample.GetProperty("errors").EnumerateArray());
            Assert.Equal(command == "shares" ? 2 : 1, sample.GetProperty("records").GetArrayLength());
            Assert.Contains(sample.GetProperty("trace").EnumerateArray(), item => item.GetString()!.EndsWith(":finally", StringComparison.Ordinal));
            if (command == "disk") Assert.Equal(2, sample.GetProperty("records")[0].GetProperty("SizeGB").GetInt32());
            if (command == "features") Assert.Equal("Enabled", sample.GetProperty("records")[0].GetProperty("InstallState").GetString());
            if (command == "shares") Assert.True(sample.GetProperty("interopLoaded").GetBoolean());
        }
        foreach (var command in new[] { "cpu", "device", "ram", "startup" })
        {
            var observations = expected.Select(line => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(line));
            var sample = Assert.Single(observations, item => item.GetProperty("command").GetString() == command &&
                item.GetProperty("count").GetInt32() == 2 && item.GetProperty("fault").GetString() == "none" &&
                !item.GetProperty("all").GetBoolean() && !item.GetProperty("extended").GetBoolean());
            Assert.Equal(2, sample.GetProperty("records").GetArrayLength());
            Assert.Empty(sample.GetProperty("errors").EnumerateArray());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, sample.GetProperty("caught").ValueKind);
            Assert.Contains(sample.GetProperty("trace").EnumerateArray(), item =>
                item.GetString()!.EndsWith(":finally", StringComparison.Ordinal));
            var first = sample.GetProperty("records")[0];
            switch (command)
            {
                case "cpu": Assert.Equal("cpu1", first.GetProperty("Name").GetString()); break;
                case "device": Assert.Equal("fixture", first.GetProperty("DeviceClass").GetString()); break;
                case "ram": Assert.Equal(2.0, first.GetProperty("Size").GetDouble()); break;
                case "startup": Assert.Equal("startup1", first.GetProperty("Caption").GetString()); break;
            }
        }
        Assert.True(expected.SequenceEqual(actual),
            $"Original JSON lines: {expected.Length}; generated JSON lines: {actual.Length}." + Environment.NewLine +
            string.Join(Environment.NewLine, expected.Zip(actual)
                .Where(pair => pair.First != pair.Second).Take(5)
                .Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)) +
            Environment.NewLine + "Generated output tail: " + compiled.StandardOutput.Substring(
                Math.Max(0, compiled.StandardOutput.Length - 1200)) +
            Environment.NewLine + "Generated error tail: " + compiled.StandardError.Substring(
                Math.Max(0, compiled.StandardError.Length - 1200)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(original.StandardError, compiled.StandardError);
        var cancellation = "; $providerSetup=@'" + Environment.NewLine + setup + Environment.NewLine + "'@" + Environment.NewLine +
            File.ReadAllText(Path.Combine(observerRoot, "Cancellation.ps1"));
        var originalStop = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'" + cancellation,
            fixture.RootPath, "inventory-stop-original");
        var compiledStop = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'" + cancellation,
            fixture.RootPath, "inventory-stop-compiled");
        Assert.True(originalStop.ExitCode == 0, originalStop.StandardOutput + originalStop.StandardError);
        Assert.True(compiledStop.ExitCode == 0, compiledStop.StandardOutput + compiledStop.StandardError);
        Assert.Equal(6, originalStop.StandardOutput.Split('\n').Count(line => line.StartsWith("{", StringComparison.Ordinal)));
        Assert.Equal(3, originalStop.StandardOutput.Split('\n').Count(line => line.Contains("\"state\":\"Stopped\",\"calls\":1,\"cleanups\":1", StringComparison.Ordinal)));
        Assert.Equal(originalStop.StandardOutput, compiledStop.StandardOutput);
        Assert.Equal(originalStop.StandardError, compiledStop.StandardError);
    }
}
