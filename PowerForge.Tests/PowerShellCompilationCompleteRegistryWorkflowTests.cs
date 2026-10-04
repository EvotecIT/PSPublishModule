using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedRegistryPreservesModuleStateAndRetainedCalls(string framework, string host)
        => AssertPinnedRegistryWorkflow(framework, host, fullModule: false);

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedRegistryInFullModule(string framework, string host)
        => AssertPinnedRegistryWorkflow(framework, host, fullModule: true);

    private static void AssertPinnedRegistryWorkflow(string framework, string host, bool fullModule)
    {
        var sources = new[] {
            ("Get-PSRegistry.ps1", "eab1e288ef253144860ed0e4c1e5f46d3e751fd0028ece44c343dbff65cf48de"),
            ("Script.RegistryDictionaries.ps1", "aec4baa3a3287d98974c8e1cac3f7a7cc6d7ab37bdbb135275e9e7a4fb7ce133"),
            ("Resolve-PrivateRegistry.ps1", "b038a01d81bbfcf4ea9590c5f5c7875dfc14d3e17e891557b69f78e1cf41858d"),
            ("Get-ComputerSplit.ps1", "7f7fa3d72dc221ae9045c402bb00076b1f6dc87f41f1c8d2dd3884e1ff9ec2d6"),
            ("Get-PSConvertSpecialRegistry.ps1", "41a04d8c8b319f02448dd3cd3d9dcdbf5bdd05baa62dd56fc762ffa0507278ae"),
            ("Get-PSSubRegistryTranslated.ps1", "eecf5180f27a51ace4ec0f1d0338a50b9c45fedf127a7ba37e79b35628b3a472"),
            ("ConvertTo-HKeyUser.ps1", "cc23aa9c79c53463e9f84fa32380dd046555edd67fdaf6a548ed76504406a991"),
            ("Unregister-MountedRegistry.ps1", "1b4d9c64aa461a1b1eedb90c0630cd4b3f042bac838b6b3057a66a41237869d2")
        };
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine,
            sources.Select(source => ". \"$PSScriptRoot/Dependencies/" + source.Item1 + "\"")), ".psm1");
        var dependencyRoot = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(fixture.ScriptPath)!, "Dependencies")).FullName;
        foreach (var source in sources)
        {
            var path = FindCompleteConversionWorkflow("PSSharedGoods", "Registry", source.Item1);
            Assert.Equal(source.Item2, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            if (!fullModule)
                File.Copy(path, Path.Combine(dependencyRoot, source.Item1));
        }
        if (fullModule)
            CopyPinnedWorkflowModule(fixture, "PSSharedGoods", "87f61869110c6e6950603027fe4c9304d4d7a4386008754e009710cabae37ea6", 285);
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        if (fullModule)
            Assert.Equal(284, resolved.SourceFiles.Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath, fixture.OutputPath, "Generated.CompleteRegistry", resolved.Kind,
            resolved.Mode, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework,
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            RuntimeSourcePaths = resolved.SourceFiles,
            ModuleManifestPath = resolved.ModuleManifestPath
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var units = result.Manifest!.UnitDispositionLedger!.Entries;
        var plan = new PowerShellCompilationAnalyzer().Analyze(resolved, resolved.Mode, framework);
        var explanation = PowerShellCompilationExplainShaper.CreateFinalExplanation(resolved, plan, framework);
        foreach (var unit in units.Where(unit => unit.RegionGraph is not null))
        {
            var explained = Assert.Single(explanation.Files.SelectMany(file => file.Units), item => item.Name == unit.Name);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(unit.RegionGraph),
                System.Text.Json.JsonSerializer.Serialize(explained.RegionGraph));
        }
        foreach (var name in new[] { "Get-PSRegistry", "Get-PSRegistryDictionaries", "Get-ComputerSplit", "Get-PSConvertSpecialRegistry", "Get-PSSubRegistryTranslated", "ConvertTo-HkeyUser", "Unregister-MountedRegistry", "Resolve-PrivateRegistry" })
        {
            var unit = Assert.Single(units, item => item.Name == name);
            Assert.True(unit.EmittedClrMethod, name + ": " + string.Join("; ", unit.DiagnosticChain.Select(cause => cause.Message)));
            Assert.False(unit.RetainedHostedSource);
        }
        var entry = Assert.Single(units, item => item.Name == "Get-PSRegistry");
        Assert.True(entry.RuntimeCommandRegions > 1);
        Assert.Contains(units, unit => unit.RetainedHostedSource);
        var graph = Assert.IsType<PowerShellCompilationRegionGraph>(entry.RegionGraph);
        Assert.Contains(graph.Regions.SelectMany(region => region.Mutations), name => name == "PowerShellSessionVariable:SCRIPT:CURRENTGETCOUNT");
        var observerRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PowerShellCompilationRegistryWorkflow");
        var setup = File.ReadAllText(Path.Combine(observerRoot, "Providers.ps1"));
        var observer = File.ReadAllText(Path.Combine(observerRoot, "Observe.ps1"));
        var original = RunStatementErrorProbe(host, "$module=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + setup + Environment.NewLine + observer, fixture.RootPath, "registry-workflow");
        var compiled = RunStatementErrorProbe(host, "$module=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + setup + Environment.NewLine + observer, fixture.RootPath, "registry-workflow");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(433, expected.Count(line => line.StartsWith("{", StringComparison.Ordinal)));
        Assert.True(original.StandardOutput.Contains("dismount:HKEY_USERS", StringComparison.Ordinal),
            string.Join(Environment.NewLine, expected.Where(line => line.StartsWith("{", StringComparison.Ordinal)).Take(2)));
        Assert.Contains("depth=2", original.StandardOutput, StringComparison.Ordinal);
        AssertRegistryAcquiredState(expected);
        Assert.True(expected.Length == actual.Length,
            "Generated registry output length differs." + Environment.NewLine +
            "Output:" + Environment.NewLine + compiled.StandardOutput + Environment.NewLine +
            "Error:" + Environment.NewLine + compiled.StandardError);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(5).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
        var cancellation = File.ReadAllText(Path.Combine(observerRoot, "Cancellation.ps1"));
        var stopSetup = "; $providerSetup=@'" + Environment.NewLine + setup + Environment.NewLine + "'@" + Environment.NewLine + cancellation;
        var originalStop = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'" + stopSetup,
            fixture.RootPath, "registry-workflow-stop");
        var compiledStop = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'" + stopSetup,
            fixture.RootPath, "registry-workflow-stop");
        Assert.True(originalStop.ExitCode == 0, originalStop.StandardOutput + originalStop.StandardError);
        Assert.True(compiledStop.ExitCode == 0, compiledStop.StandardOutput + compiledStop.StandardError);
        Assert.Equal(6, originalStop.StandardOutput.Split('\n').Count(line => line.StartsWith("{", StringComparison.Ordinal)));
        Assert.Equal(2, originalStop.StandardOutput.Split('\n').Count(line => line.Contains("\"state\":\"Stopped\",\"calls\":1,\"cleanups\":1", StringComparison.Ordinal)));
        Assert.Equal(originalStop.StandardOutput, compiledStop.StandardOutput);
        Assert.Equal(originalStop.StandardError, compiledStop.StandardError);
    }

    private static void AssertRegistryAcquiredState(IEnumerable<string> lines)
    {
        foreach (var name in new[] { "all-default", "all-offline-default", "all-offline-cleanup" })
        {
            var observation = Assert.Single(lines, line =>
            {
                if (!line.StartsWith("{", StringComparison.Ordinal)) return false;
                using var document = System.Text.Json.JsonDocument.Parse(line);
                var root = document.RootElement;
                return root.TryGetProperty("case", out var caseName) && caseName.GetString() == name &&
                       root.GetProperty("fault").GetString() == "none" && root.GetProperty("action").GetString() == "Continue" &&
                       !root.GetProperty("stop").GetBoolean() && !root.GetProperty("initialized").GetBoolean() &&
                       !root.GetProperty("premounted").GetBoolean();
            });
            using var document = System.Text.Json.JsonDocument.Parse(observation);
            var state = document.RootElement.GetProperty("state");
            var trace = state.GetProperty("trace").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.Single(trace, item => item.StartsWith("mount:default:", StringComparison.Ordinal));
            Assert.Equal(0, state.GetProperty("counter").GetInt32());
            var keepsMounts = name == "all-offline-default";
            Assert.Equal(keepsMounts, state.GetProperty("defaultMounted").GetBoolean());
            Assert.Equal(keepsMounts, state.GetProperty("offlineMounted").GetBoolean());
            if (name != "all-default")
            {
                Assert.Single(trace, item => item.StartsWith("mount:offline:", StringComparison.Ordinal));
                Assert.Contains(document.RootElement.GetProperty("records").EnumerateArray(), record =>
                    record.GetProperty("Registry").GetString()!.Contains("Offline_Fixture", StringComparison.Ordinal));
            }
            if (keepsMounts) Assert.DoesNotContain(trace, item => item.StartsWith("dismount:", StringComparison.Ordinal));
            else
            {
                Assert.Contains(trace, item => item.StartsWith("dismount:HKEY_USERS\\.DEFAULT_USER:depth=0", StringComparison.Ordinal));
                if (name == "all-offline-cleanup")
                    Assert.Contains(trace, item => item.StartsWith("dismount:HKEY_USERS\\Offline_Fixture:depth=0", StringComparison.Ordinal));
            }
        }
    }
}
