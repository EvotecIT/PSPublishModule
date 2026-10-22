using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.IO.Compression;
using Xunit;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ApprovedModuleDependencyPruningTests
{
    [Fact]
    public void Run_FinalManifestAndArchiveContainOnlyRemainingRuntimeDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var previousModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        Directory.CreateDirectory(root);
        try
        {
            string modules = Path.Combine(root, "installed");
            WriteModule(modules, "SharedHelpers", "function Get-SharedHelper { 'shared' }; Export-ModuleMember Get-SharedHelper");
            WriteModule(modules, "ParentDonor", "function Get-SharedHelper { 'parent-copy' }; function Get-ParentThing { Get-SharedHelper }; Export-ModuleMember Get-ParentThing");
            WriteModule(modules, "RuntimeDependency", "function Get-RuntimeThing { 'runtime' }; Export-ModuleMember Get-RuntimeThing");
            Environment.SetEnvironmentVariable("PSModulePath", modules + Path.PathSeparator + previousModulePath);
            string source = Path.Combine(root, "source");
            WriteModule(root, "source", "function Get-ConsumerThing { Get-ParentThing }; Export-ModuleMember Get-ConsumerThing");
            // WriteModule uses the directory name as identity; the consumer has a normal manifest.
            var segments = new List<IConfigurationSegment>
            {
                new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { Merge = true, MergeMissing = true } },
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true, Path = Path.Combine(root, "packed"),
                        RequiredModules = new ArtefactRequiredModulesConfiguration { Enabled = true, Source = RequiredModulesSource.Installed }
                    }
                }
            };
            foreach (string name in new[] { "ParentDonor", "SharedHelpers", "RuntimeDependency" })
            {
                segments.Add(new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration { ModuleName = name, RequiredVersion = "1.0.0", VersionSource = ModuleDependencyVersionSource.Installed }
                });
                if (name != "RuntimeDependency")
                    segments.Add(new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = name, VersionSource = ModuleDependencyVersionSource.Installed }
                    });
            }
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = "source", SourcePath = source, Version = "1.0.0", KeepStaging = true },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };
            var runner = new ModulePipelineRunner(new NullLogger(), powerShellRunner: null,
                moduleDependencyMetadataProvider: new FixtureMetadataProvider(modules),
                missingFunctionAnalysisService: new PowerShellMissingFunctionAnalysisService());
            var result = runner.Run(spec, runner.Plan(spec));
            string manifest = File.ReadAllText(result.BuildResult.ManifestPath);
            Assert.Contains("RuntimeDependency", manifest);
            Assert.DoesNotContain("SharedHelpers", manifest);
            Assert.DoesNotContain("ParentDonor", manifest);
            using var zip = ZipFile.OpenRead(Path.Combine(root, "packed", "source.zip"));
            Assert.Contains(zip.Entries, entry => entry.FullName == "RuntimeDependency/RuntimeDependency.psd1");
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("SharedHelpers/", StringComparison.Ordinal));
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("ParentDonor/", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", previousModulePath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyze_PrunesApprovedDependencyWhoseHelperIsAlreadyLocal()
    {
        var service = new PowerShellMissingFunctionAnalysisService();
        var result = service.Analyze(null,
            "function Get-LocalHelper { 'local' }; Get-LocalHelper",
            new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" }));

        Assert.Equal(new[] { "SharedHelpers" }, result.FullyInlinedApprovedModules);
        Assert.Empty(result.Functions);
    }

    [Fact]
    public void Analyze_PrunesRedundantApprovedDependencyWhenAnotherDonorContainsItsHelper()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteModule(root, "SharedHelpers", "function Get-SharedHelper { 'shared' }; Export-ModuleMember Get-SharedHelper");
            WriteModule(root, "ParentDonor", "function Get-SharedHelper { 'parent-copy' }; function Get-ParentThing { Get-SharedHelper }; Export-ModuleMember Get-ParentThing");
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, "Get-ParentThing",
                new MissingFunctionsOptions(
                    approvedModules: new[] { "ParentDonor", "SharedHelpers" },
                    includeFunctionsRecursively: true,
                    approvedModuleSources: new[]
                    {
                        new ApprovedModuleSource("ParentDonor", "1.0.0", Path.Combine(root, "ParentDonor")),
                        new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers"))
                    },
                    requireApprovedModuleSources: true));

            Assert.Equal(new[] { "ParentDonor", "SharedHelpers" }, result.FullyInlinedApprovedModules);
            Assert.Contains(result.Functions, function => function.Contains("parent-copy", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Functions, function => function.Contains("'shared'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyze_PreservesApprovedDependenciesWhenDynamicInvocationPreventsProof()
    {
        var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, "& $commandName",
            new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" }));

        Assert.Empty(result.FullyInlinedApprovedModules);
    }

    private static void WriteModule(string root, string name, string body)
    {
        var moduleRoot = Path.Combine(root, name);
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, name + ".psm1"), body);
        File.WriteAllText(Path.Combine(moduleRoot, name + ".psd1"),
            $"@{{RootModule='{name}.psm1'; ModuleVersion='1.0.0'}}");
    }

    private sealed class FixtureMetadataProvider(string modulesRoot) : IModuleDependencyVersionedMetadataProvider
    {
        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetInstalledModules(IReadOnlyList<RequiredModuleReference> references)
            => GetLatestInstalledModules(references.Select(reference => reference.ModuleName).ToArray());

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => names.ToDictionary(name => name, name => new InstalledModuleMetadata(name, "1.0.0", null, Path.Combine(modulesRoot, name)), StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<RequiredModuleReference>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names, string? repository, RepositoryCredential? credential, bool prerelease)
            => throw new InvalidOperationException("The fixture must resolve installed dependencies without network access.");
    }
}
