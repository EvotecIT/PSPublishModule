using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

public sealed partial class ModuleBootstrapperSharedAssemblyTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ModuleBootstrapperSharedAssemblyTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("../SharedCore")]
    [InlineData("SharedCore.dll")]
    [InlineData("Shared*")]
    [InlineData("System.Management.Automation")]
    [InlineData("Microsoft.PowerShell.Commands.Utility")]
    [InlineData("mscorlib")]
    [InlineData("SharedCore, Version=1.0.0.0")]
    public void SharedPolicy_UnsafeOrHostAssemblyName_IsRejected(string name)
        => Assert.Throws<ArgumentException>(() => SharedAssemblyOptions.Validate(new[] { name }, null, null));

    [Fact]
    public void SharedPolicy_ConflictingExportOrUndeclaredPreload_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => SharedAssemblyOptions.Validate(new[] { "SharedCore" }, null, new[] { "SharedCore.dll" }));
        Assert.Throws<ArgumentException>(() => SharedAssemblyOptions.Validate(new[] { "SharedCore" }, new[] { "Other" }, null));
        SharedAssemblyOptions.Validate(Array.Empty<string>(), Array.Empty<string>(), new[] { "Product.dll" });
    }

    [Fact]
    public void Generate_ImplicitBinaryExportCannotBeShared()
    {
        var root = NewScratchRoot();
        try
        {
            Assert.Throws<ArgumentException>(() => ModuleBootstrapperGenerator.Generate(root, "Product",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()), null,
                handleRuntimes: false, sharedAssemblies: new[] { "Product" }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BuildConfiguration_SharedNames_RoundTripIntoPipelinePlan()
    {
        var root = NewScratchRoot();
        try
        {
            var segments = new BuildConfigurationFactory().Create(new BuildConfigurationRequest
            {
                NETSharedAssembliesSpecified = true,
                NETSharedAssemblies = new[] { "SharedCore", "SharedDisplay" },
                NETPreloadSharedAssembliesSpecified = true,
                NETPreloadSharedAssemblies = new[] { "SharedCore" }
            });
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = "Product", SourcePath = root, Version = "1.0.0" },
                Segments = segments.ToArray(),
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new ConfigurationSegmentJsonConverter());
            var copy = JsonSerializer.Deserialize<ModulePipelineSpec>(JsonSerializer.Serialize(spec, options), options)!;
            var plan = new ModulePipelineRunner(new NullLogger()).Plan(copy);
            Assert.Equal(new[] { "SharedCore", "SharedDisplay" }, plan.BuildSpec.SharedAssemblies);
            Assert.Equal(new[] { "SharedCore" }, plan.BuildSpec.PreloadSharedAssemblies);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedSharedPolicy_ThreeProducts_AllImportOrdersShareOneRuntimeAndKeepDataAvailable()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = NewScratchRoot();
        var fixtureScript = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SharedModuleAssemblies", "New-Fixtures.ps1");
        var probeScript = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SharedModuleAssemblies", "Test-ImportOrders.ps1");
        var fixtures = Path.Combine(root, "fixtures");
        try
        {
            Run("powershell.exe", root, "-File", fixtureScript, "-Root", fixtures);
            var products = new[] { "Alpha", "Beta", "Gamma" };
            var modules = Path.Combine(root, "modules");
            foreach (var product in products)
            {
                var moduleRoot = Path.Combine(modules, product);
                foreach (var payload in new[] { "Core", "Default" })
                {
                    var lib = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", payload)).FullName;
                    foreach (var file in Directory.GetFiles(Path.Combine(fixtures, product), "*.dll"))
                        File.Copy(file, Path.Combine(lib, Path.GetFileName(file)));
                    CopyRuntime(fixtures, lib, feature: 1);
                }
                ModuleBootstrapperGenerator.Generate(moduleRoot, product,
                    new ExportSet(Array.Empty<string>(), new[] { "Get-" + product + "Data" }, Array.Empty<string>()),
                    new[] { product + ".Host.dll" }, handleRuntimes: false, useAssemblyLoadContext: true,
                    ignoreLibrariesOnLoad: new[] { product + ".Presentation.dll" },
                    targetFrameworks: new[] { "net8.0", "net472" },
                    developmentBinaries: new ModuleDevelopmentBinaryBootstrapperOptions(
                        ModuleDevelopmentBinaryMode.Environment, Path.Combine(fixtures, product, "bin"),
                        "PF_SHARED_FIXTURE_DEVELOPMENT", "PF_SHARED_FIXTURE_CONFIGURATION", new[] { "net8.0" }, new[] { "net472" }),
                    sharedAssemblies: new[] { "SharedCore", "SharedDisplay" },
                    preloadSharedAssemblies: new[] { "SharedCore" });
                foreach (var framework in new[] { "net8.0", "net472" })
                {
                    var output = Directory.CreateDirectory(Path.Combine(fixtures, product, "bin", "Release", framework)).FullName;
                    foreach (var file in Directory.GetFiles(Path.Combine(fixtures, product), "*.dll"))
                        File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
                    CopyRuntime(fixtures, output, 1);
                }
            }
            // Development source loading must honor the same shared-first Core policy.
            foreach (var host in new[] { "powershell.exe", "pwsh.exe" })
            {
                var output = Run(host, root, "-File", probeScript, "-Root", modules, "-Order", "Alpha,Beta,Gamma", "-ExpectedFeature", "1", "-Development");
                Assert.Contains("SHARED_IMPORT_OK:", output, StringComparison.Ordinal);
                _output.WriteLine("development: " + output.Trim());
                output = Run(host, root, "-File", probeScript, "-Root", modules, "-Order", "Alpha,Beta,Gamma", "-ExpectedFeature", "1", "-ExistingRuntimeRoot", fixtures);
                Assert.Contains("SHARED_IMPORT_OK:", output, StringComparison.Ordinal);
                _output.WriteLine("existing older Display: " + output.Trim());
            }
            foreach (var feature in new[] { 0, 1 })
            {
                var output = Run("pwsh.exe", root, "-File", probeScript, "-Root", modules, "-Order", "Alpha,Beta,Gamma",
                    "-ExpectedFeature", feature.ToString(), "-ExistingPrivateRuntimeRoot", fixtures, "-ExistingPrivateFeature", feature.ToString());
                Assert.Contains("SHARED_IMPORT_OK:", output, StringComparison.Ordinal);
                _output.WriteLine("existing private core: " + output.Trim());
            }

            var orders = new[] { "Alpha,Beta,Gamma", "Alpha,Gamma,Beta", "Beta,Alpha,Gamma", "Beta,Gamma,Alpha", "Gamma,Alpha,Beta", "Gamma,Beta,Alpha" };
            foreach (var scenario in new[] { "compatible", "feature-gap" })
            {
                foreach (var product in products)
                    foreach (var payload in new[] { "Core", "Default" })
                        CopyRuntime(fixtures, Path.Combine(modules, product, "Lib", payload), scenario == "feature-gap" && product != "Gamma" ? 0 : 1);
                foreach (var host in new[] { "powershell.exe", "pwsh.exe" })
                    foreach (var order in orders)
                    {
                        var expectedFeature = scenario == "feature-gap" && !order.StartsWith("Gamma", StringComparison.Ordinal) ? 0 : 1;
                        var output = Run(host, root, "-File", probeScript, "-Root", modules, "-Order", order, "-ExpectedFeature", expectedFeature.ToString());
                        Assert.Contains("SHARED_IMPORT_OK:", output, StringComparison.Ordinal);
                        _output.WriteLine(scenario + ": " + output.Trim());
                    }
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CopyRuntime(string fixtures, string destination, int feature)
    {
        foreach (var name in new[] { "SharedCore", "SharedDisplay" })
            File.Copy(Path.Combine(fixtures, "Core" + feature, name + ".dll"), Path.Combine(destination, name + ".dll"), true);
    }

    private static string NewScratchRoot()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-shared-" + Guid.NewGuid().ToString("N"))).FullName;

    private static string Run(string executable, string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }.Concat(arguments))
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            if (!process.WaitForExit(60000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                Assert.Fail("Owned fixture process exceeded its deadline: " + executable);
            }
            Task.WaitAll(stdout, stderr);
            Assert.True(process.ExitCode == 0, executable + " " + string.Join(" ", arguments) + Environment.NewLine + stdout.Result + stderr.Result);
            return stdout.Result;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
        }
    }
}
