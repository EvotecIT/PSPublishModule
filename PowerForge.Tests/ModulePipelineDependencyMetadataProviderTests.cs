using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineDependencyMetadataProviderTests
{
    [Fact]
    public void Plan_UsesInjectedDependencyMetadataProvider_ForAutoGuidResolution()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string dependencyName = "PSSharedGoods";
            const string dependencyGuid = "11111111-2222-3333-4444-555555555555";

            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = dependencyName,
                            ModuleVersion = "0.25.0",
                            Guid = "Auto"
                        }
                    }
                }
            };

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = ("0.30.0", dependencyGuid)
                });

            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);
            var plan = runner.Plan(spec);

            var required = Assert.Single(plan.RequiredModules);
            Assert.Equal(dependencyGuid, required.Guid);
            Assert.Equal(1, provider.InstalledLookups);
            Assert.Equal(1, provider.OnlineLookups);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_ReordersRequiredModules_ForBinaryConflictOrder_UsingInjectedDependencyMetadataProvider()
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

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Alpha.Tools"] = new("Alpha.Tools", "1.0.0", null, alphaModuleBase.Parent!.FullName),
                    ["Beta.Tools"] = new("Beta.Tools", "2.0.0", null, betaModuleBase.Parent!.FullName)
                },
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase));

            var logger = new CollectingLogger();
            var runner = new ModulePipelineRunner(logger, new ThrowingPowerShellRunner(), provider);

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
            Assert.Equal(2, provider.InstalledLookups);
            Assert.Contains(logger.Infos, static message => message.Contains("PreferBinaryConflictOrder reordered RequiredModules", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void MissingAnalysis_ResolvesDependentModules_UsingInjectedDependencyMetadataProvider()
    {
        var provider = new FakeModuleDependencyMetadataProvider(
            installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
            onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
            installedRequiredModules: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Alpha.Tools"] = new[] { "Beta.Tools", "Gamma.Tools" },
                ["Beta.Tools"] = new[] { "Gamma.Tools", "Delta.Tools" },
                ["Gamma.Tools"] = Array.Empty<string>(),
                ["Delta.Tools"] = Array.Empty<string>()
            });

        var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);
        var method = typeof(ModulePipelineRunner).GetMethod("ResolveDependentRequiredModules", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(method is not null, "ResolveDependentRequiredModules method signature may have changed.");

        var dependencies = (string[])method!.Invoke(
            runner,
            new object?[]
            {
                new[] { "Alpha.Tools" },
                Array.Empty<string>()
            })!;

        Assert.Equal(3, dependencies.Length);
        Assert.Contains("Beta.Tools", dependencies);
        Assert.Contains("Gamma.Tools", dependencies);
        Assert.Contains("Delta.Tools", dependencies);
        Assert.Equal(4, provider.RequiredModuleLookups);
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
            Arguments = $"build \"{projectPath}\" -c Release -nologo --verbosity quiet",
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

    private sealed class FakeModuleDependencyMetadataProvider : IModuleDependencyMetadataProvider
    {
        private readonly IReadOnlyDictionary<string, InstalledModuleMetadata> _installedModules;
        private readonly IReadOnlyDictionary<string, (string? Version, string? Guid)> _onlineModules;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _installedRequiredModules;

        internal int InstalledLookups { get; private set; }
        internal int OnlineLookups { get; private set; }
        internal int RequiredModuleLookups { get; private set; }

        internal FakeModuleDependencyMetadataProvider(
            IReadOnlyDictionary<string, InstalledModuleMetadata> installedModules,
            IReadOnlyDictionary<string, (string? Version, string? Guid)> onlineModules,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? installedRequiredModules = null)
        {
            _installedModules = installedModules;
            _onlineModules = onlineModules;
            _installedRequiredModules = installedRequiredModules ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
        {
            InstalledLookups++;
            var result = new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names ?? Array.Empty<string>())
            {
                if (_installedModules.TryGetValue(name, out var module))
                    result[name] = module;
                else
                    result[name] = new InstalledModuleMetadata(name, null, null, null);
            }

            return result;
        }

        public IReadOnlyList<string> GetRequiredModulesForInstalledModule(string moduleName)
        {
            RequiredModuleLookups++;
            if (string.IsNullOrWhiteSpace(moduleName))
                return Array.Empty<string>();

            return _installedRequiredModules.TryGetValue(moduleName, out var modules)
                ? modules
                : Array.Empty<string>();
        }

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
        {
            OnlineLookups++;
            var result = new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names ?? Array.Empty<string>())
            {
                if (_onlineModules.TryGetValue(name, out var module))
                    result[name] = module;
            }

            return result;
        }
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell runner should not be used when dependency metadata provider is injected.");
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Infos { get; } = new();
        public bool IsVerbose => false;

        public void Info(string message) => Infos.Add(message ?? string.Empty);
        public void Success(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}
