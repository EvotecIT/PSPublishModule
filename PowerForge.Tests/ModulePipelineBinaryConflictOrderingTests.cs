using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PowerForge.Tests;

public sealed class ModulePipelineBinaryConflictOrderingTests
{
    [Theory]
    [InlineData("Direct")]
    [InlineData("Nested")]
    [InlineData("Intermediate")]
    [InlineData("InstalledAfterPlanning")]
    [InlineData("Script")]
    public void Plan_PlacesSelectedDependencyBeforeModuleThatRequiresIt(string route)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteMinimalModule(root.FullName, "TestModule", "1.0.0");
            var consumer = Path.Combine(root.FullName, "Consumer.Tools");
            var runtime = Path.Combine(root.FullName, "Runtime.Tools");
            WriteMinimalModule(consumer, "Consumer.Tools", "1.0.0");
            WriteMinimalModule(runtime, "Runtime.Tools", "1.0.0");
            const string requiresRuntime = "RequiredModules = @(@{ ModuleName = 'Runtime.Tools'; ModuleVersion = '1.0.0' })";
            var modules = new Dictionary<string, string>
            {
                ["Consumer.Tools"] = consumer,
                ["Runtime.Tools"] = runtime
            };
            var consumerRequirements = requiresRuntime;
            string? deferredIntermediate = null;
            if (route != "Direct" && route != "Script")
            {
                var intermediate = route == "Nested"
                    ? Path.Combine(consumer, "Intermediate.Tools")
                    : Path.Combine(root.FullName, "Intermediate.Tools");
                WriteMinimalModule(intermediate, "Intermediate.Tools", "1.0.0");
                File.WriteAllText(Path.Combine(intermediate, "Intermediate.Tools.psd1"),
                    "@{ RootModule = 'Intermediate.Tools.psm1'; ModuleVersion = '1.0.0'; " + requiresRuntime + " }");
                if (route == "InstalledAfterPlanning")
                    deferredIntermediate = intermediate;
                else
                    modules["Intermediate.Tools"] = intermediate;
                consumerRequirements = route == "Nested"
                    ? "NestedModules = @('Intermediate.Tools/Intermediate.Tools.psd1')"
                    : "RequiredModules = @('Intermediate.Tools')";
            }
            File.WriteAllText(Path.Combine(consumer, "Consumer.Tools.psd1"),
                "@{ RootModule = 'Consumer.Tools.psm1'; ModuleVersion = '1.0.0'; " + consumerRequirements + " }");
            if (route == "Script")
            {
                File.Delete(Path.Combine(consumer, "Consumer.Tools.psd1"));
                File.WriteAllText(Path.Combine(consumer, "Consumer.Tools.psm1"),
                    "#requires -Modules @{ ModuleName = 'Runtime.Tools'; ModuleVersion = '1.0.0' }\n");
            }
            var runner = new ModulePipelineRunner(new NullLogger(), new StubPowerShellRunner(modules));
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = "TestModule", SourcePath = root.FullName, Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { InstallMissingModules = false } },
                    new ConfigurationModuleSegment { Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "Consumer.Tools", RequiredVersion = "1.0.0" } },
                    new ConfigurationModuleSegment { Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "Runtime.Tools", RequiredVersion = "1.0.0" } },
                    new ConfigurationImportModulesSegment { ImportModules = new ImportModulesConfiguration
                        { RequiredModules = true, PreferBinaryConflictOrder = true } }
                }
            };

            var plan = runner.Plan(spec);

            if (deferredIntermediate is not null)
            {
                Assert.Equal(new[] { "Consumer.Tools", "Runtime.Tools" }, plan.RequiredModules.Select(module => module.ModuleName));
                // Model metadata becoming available during dependency preparation, after pure planning.
                modules["Intermediate.Tools"] = deferredIntermediate;
                typeof(ModulePipelineRunner).GetMethod("EnsureBuildDependenciesInstalledIfNeeded",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(runner, new object[] { plan });
            }

            Assert.Equal(new[] { "Runtime.Tools", "Consumer.Tools" }, plan.RequiredModules.Select(module => module.ModuleName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_PreservesInstalledConflictAnalysisOptIn()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    AnalyzeInstalledBinaryConflictsDuringBuild = true,
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);

            Assert.True(plan.BuildSpec.AnalyzeInstalledBinaryConflictsDuringBuild);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_ReordersSelectedRequiredModules_WhenPreferBinaryConflictOrderEnabled(bool newerUnselectedVersionInstalled)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var alphaAssembly = BuildLibrary(root.FullName, "SharedAuth", "1.0.0", projectFolderName: "SharedAuth_Alpha");
            var betaAssembly = BuildLibrary(root.FullName, "SharedAuth", "2.0.0", projectFolderName: "SharedAuth_Beta");

            var alphaModuleBase = Directory.CreateDirectory(Path.Combine(root.FullName, "Alpha.Tools", "1.0.0", "bin"));
            File.Copy(alphaAssembly, Path.Combine(alphaModuleBase.FullName, "SharedAuth.dll"), overwrite: true);

            var betaModuleBase = Directory.CreateDirectory(Path.Combine(root.FullName, "Beta.Tools", "2.0.0", "bin"));
            File.Copy(betaAssembly, Path.Combine(betaModuleBase.FullName, "SharedAuth.dll"), overwrite: true);

            var latestAlphaModuleBase = alphaModuleBase.Parent!.FullName;
            if (newerUnselectedVersionInstalled)
            {
                var latestAssembly = BuildLibrary(root.FullName, "SharedAuth", "3.0.0", projectFolderName: "SharedAuth_Unselected");
                var latestBin = Directory.CreateDirectory(Path.Combine(root.FullName, "Alpha.Tools", "3.0.0", "bin"));
                File.Copy(latestAssembly, Path.Combine(latestBin.FullName, "SharedAuth.dll"));
                latestAlphaModuleBase = latestBin.Parent!.FullName;
            }

            var logger = new CollectingLogger();
            var selectedModules = new Dictionary<string, string>
            {
                ["Alpha.Tools"] = alphaModuleBase.Parent!.FullName,
                ["Beta.Tools"] = betaModuleBase.Parent!.FullName
            };
            var latestModules = new Dictionary<string, string>(selectedModules)
            {
                ["Alpha.Tools"] = latestAlphaModuleBase
            };
            var runner = new ModulePipelineRunner(logger, new StubPowerShellRunner(latestModules, selectedModules));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Alpha.Tools",
                            RequiredVersion = "1.0.0"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Beta.Tools",
                            RequiredVersion = "2.0.0"
                        }
                    },
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration
                        {
                            RequiredModules = true,
                            PreferBinaryConflictOrder = true
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);

            Assert.Equal(new[] { "Beta.Tools", "Alpha.Tools" }, plan.RequiredModules.Select(static module => module.ModuleName).ToArray());
            Assert.Contains(logger.Infos, static message => message.Contains("PreferBinaryConflictOrder reordered RequiredModules", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), string.Empty);

        var psd1 = string.Join(Environment.NewLine, new[]
        {
            "@{",
            $"    RootModule = '{moduleName}.psm1'",
            $"    ModuleVersion = '{version}'",
            "    FunctionsToExport = @()",
            "    CmdletsToExport = @()",
            "    AliasesToExport = @()",
            "}"
        }) + Environment.NewLine;

        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psd1"), psd1);
    }

    private static string BuildLibrary(string rootPath, string assemblyName, string version, string projectFolderName)
    {
        var projectRoot = Directory.CreateDirectory(Path.Combine(rootPath, projectFolderName));
        var projectPath = Path.Combine(projectRoot.FullName, assemblyName + ".csproj");
        var sourcePath = Path.Combine(projectRoot.FullName, "Class1.cs");

        File.WriteAllText(projectPath, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>{{assemblyName}}</AssemblyName>
    <Version>{{version}}</Version>
    <AssemblyVersion>{{version}}.0</AssemblyVersion>
    <FileVersion>{{version}}.0</FileVersion>
  </PropertyGroup>
</Project>
""");

        File.WriteAllText(sourcePath, $$"""
namespace {{assemblyName}}Lib;

public sealed class Marker
{
}
""");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release -nologo --verbosity quiet -nr:false",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectRoot.FullName
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"dotnet build failed for test fixture.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        var assemblyPath = Path.Combine(projectRoot.FullName, "bin", "Release", "net8.0", assemblyName + ".dll");
        Assert.True(File.Exists(assemblyPath), $"Built assembly not found: {assemblyPath}");
        return assemblyPath;
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly IReadOnlyDictionary<string, string> _moduleBasePaths;
        private readonly IReadOnlyDictionary<string, string>? _selectedModuleBasePaths;

        public StubPowerShellRunner(IReadOnlyDictionary<string, string> moduleBasePaths,
            IReadOnlyDictionary<string, string>? selectedModuleBasePaths = null)
        {
            _moduleBasePaths = moduleBasePaths;
            _selectedModuleBasePaths = selectedModuleBasePaths;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var modules = request.Arguments.Count > 1 && _selectedModuleBasePaths is not null
                ? _selectedModuleBasePaths
                : _moduleBasePaths;
            var lines = modules
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static item =>
                    "PFMODINFO::ITEM::" +
                    Encode(item.Key) + "::" +
                    Encode(string.Empty) + "::" +
                    Encode(string.Empty) + "::" +
                    Encode(item.Value));

            return new PowerShellRunResult(0, string.Join(Environment.NewLine, lines), string.Empty, "pwsh.exe");
        }

        private static string Encode(string value)
            => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Successes { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public List<string> VerboseMessages { get; } = new();

        public bool IsVerbose => false;

        public void Info(string message) => Infos.Add(message ?? string.Empty);
        public void Success(string message) => Successes.Add(message ?? string.Empty);
        public void Warn(string message) => Warnings.Add(message ?? string.Empty);
        public void Error(string message) => Errors.Add(message ?? string.Empty);
        public void Verbose(string message) => VerboseMessages.Add(message ?? string.Empty);
    }
}
