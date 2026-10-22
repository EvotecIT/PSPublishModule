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
    [Theory]
    [InlineData("$script:SharedSetting='shared'; Export-ModuleMember -Variable SharedSetting", "$SharedSetting")]
    [InlineData("$script:SharedSetting='shared'; function Get-SharedHelper { 'shared' }; Export-ModuleMember -Function Get-SharedHelper -Variable SharedSetting", "Get-SharedHelper; $SharedSetting")]
    [InlineData("function Get-SharedHelper { 'shared' }; Set-Alias SharedAlias Get-SharedHelper; Export-ModuleMember -Function Get-SharedHelper -Alias SharedAlias", "Get-Alias SharedAlias")]
    public void Analyze_PreservesDonorsWithExportedRuntimeState(string body, string code)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            WriteModule(root, "SharedHelpers", body);
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, code,
                new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" },
                    approvedModuleSources: new[] { new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers")) },
                    requireApprovedModuleSources: true));
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Analyze_PreservesBinaryRuntimeSurfaceWithoutCommandReferences(bool nested, bool required)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var initialState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
            initialState.AuthorizationManager = new System.Management.Automation.AuthorizationManager("PowerForge.Tests");
            using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialState);
            runspace.Open();
            using var ps = System.Management.Automation.PowerShell.Create();
            ps.Runspace = runspace;
            var command = Assert.IsAssignableFrom<System.Management.Automation.CmdletInfo>(
                Assert.Single(ps.AddCommand("Get-Command").AddParameter("Name", "Get-Item").Invoke()).BaseObject);
            string binary = command.ImplementingType.Assembly.Location.Replace("'", "''");
            WriteModule(root, "SharedHelpers", "Export-ModuleMember");
            string providerManifest = Path.Combine(root, "SharedHelpers", "Provider.psd1");
            File.WriteAllText(providerManifest, "@{RootModule='" + binary + "';ModuleVersion='1.0.0'}");
            File.WriteAllText(Path.Combine(root, "SharedHelpers", "SharedHelpers.psd1"), required
                ? "@{RootModule='SharedHelpers.psm1';ModuleVersion='1.0.0';RequiredModules=@('" + providerManifest.Replace("'", "''") + "')}"
                : nested
                ? "@{RootModule='SharedHelpers.psm1';ModuleVersion='1.0.0';NestedModules=@('" + binary + "')}"
                : "@{RootModule='" + binary + "';ModuleVersion='1.0.0'}");
            ps.Commands.Clear();
            var imported = Assert.IsType<System.Management.Automation.PSModuleInfo>(Assert.Single(
                ps.AddCommand("Import-Module").AddParameter("Name", Path.Combine(root, "SharedHelpers", "SharedHelpers.psd1"))
                    .AddParameter("PassThru").AddParameter("Force").AddParameter("ErrorAction", "Stop").Invoke()).BaseObject);
            Assert.Equal("SharedHelpers", imported.Name);
            Assert.True(imported.ModuleType == System.Management.Automation.ModuleType.Binary ||
                imported.NestedModules.Concat(imported.RequiredModules).Any(module => module.ModuleType == System.Management.Automation.ModuleType.Binary));
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, "Get-Item Demo:\\x",
                new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" },
                    approvedModuleSources: new[] { new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers")) },
                    requireApprovedModuleSources: true));
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("(Get-Module 'SharedHelpers').Version")]
    [InlineData("Get-Module -ListAvailable -Name 'Shared*'")]
    [InlineData("Microsoft.PowerShell.Core\\Get-Module -Name SharedHelpers")]
    [InlineData("gmo SharedHelpers")]
    [InlineData("Remove-Module -Name SharedHelpers")]
    [InlineData("rmo SharedHelpers")]
    [InlineData("Get-Module -Name $moduleName")]
    [InlineData("Get-Command -Module SharedHelpers")]
    [InlineData("Get-Command -Mod SharedHelpers")]
    [InlineData("Get-Command @parameters")]
    public void Analyze_PreservesApprovedModuleIdentityUsedByModuleManagement(string code)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            WriteModule(root, "SharedHelpers", "function Get-SharedHelper { 'shared' }; Export-ModuleMember Get-SharedHelper");
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, code,
                new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" },
                    approvedModuleSources: new[] { new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers")) },
                    requireApprovedModuleSources: true));
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("@(\"$env:PF_DEPENDENCY\")")]
    [InlineData("@(@{ModuleName=\"$env:PF_DEPENDENCY\";ModuleVersion='1.0.0'})")]
    [InlineData("@($env:PF_DEPENDENCY)")]
    public void Run_PreservesApprovedDependenciesWhenNestedRequirementsAreComputed(string requirements)
        => AssertRetainedDependency("NestedModules", "Nested.psd1", "'nested'", nestedRequirements: requirements);

    [Theory]
    [InlineData("NestedModules", "@($env:PF_NESTED_MODULE)")]
    [InlineData("ScriptsToProcess", "@($env:PF_HOOK)")]
    [InlineData("RootModule", "$env:PF_ROOT_MODULE")]
    public void Run_PreservesApprovedDependenciesWhenManifestLoadedContentIsComputed(string property, string expression)
        => AssertRetainedDependency("NestedModules", "Nested.psd1", "'nested'", nestedRequirements: "@()",
            computedProperty: property, computedExpression: expression);

    [Theory]
    [InlineData("ScriptsToProcess", "Classes/Initialize.ps1", "SharedHelpers\\Get-SharedHelper")]
    [InlineData("ScriptsToProcess", "Classes/Initialize.ps1", "Get-SharedHelper")]
    [InlineData("NestedModules", "Nested.psm1", "SharedHelpers\\Get-SharedHelper")]
    [InlineData("NestedModules", "Nested.psm1", "Get-SharedHelper")]
    [InlineData("NestedModules", "Nested.psd1", "'nested'")]
    public void Run_PreservesApprovedDependencyUsedByRetainedManifestContent(string key, string relativePath, string script)
        => AssertRetainedDependency(key, relativePath, script);

    [Theory]
    [InlineData("ScriptsToProcess", "Hooks/initialize.ps1", "Hooks/Initialize.ps1")]
    [InlineData("NestedModules", "Nested.psm1", "nested.psm1")]
    [InlineData("NestedModules", "SOURCE.psm1", null)]
    public void Run_PreservesApprovedDependencyUsedByCaseDistinctRetainedContent(
        string key, string relativePath, string? predecessor)
        => AssertRetainedDependency(key, relativePath, "SharedHelpers\\Get-SharedHelper", predecessor, requireCaseSensitiveStorage: true);

    [Fact]
    public void ReadRelativePaths_PreservesCaseDistinctReferencesForFilesystemResolution()
    {
        string manifest = Path.GetTempFileName();
        try
        {
            File.WriteAllText(manifest, "@{ScriptsToProcess=@('Hooks/Initialize.ps1','Hooks/initialize.ps1')}");
            Assert.Equal(new[] { "Hooks/Initialize.ps1", "Hooks/initialize.ps1" },
                ModuleManifestLoadedContent.ReadRelativePaths(manifest, StringComparer.Ordinal));
        }
        finally { File.Delete(manifest); }
    }

    private static void AssertRetainedDependency(string key, string relativePath, string script,
        string? predecessor = null, bool requireCaseSensitiveStorage = false,
        string nestedRequirements = "@('SharedHelpers')", string? computedProperty = null, string? computedExpression = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string modules = Path.Combine(root, "installed");
            WriteModule(modules, "SharedHelpers", "function Get-SharedHelper { 'shared' }; Export-ModuleMember Get-SharedHelper");
            WriteModule(root, "source", "function Get-ConsumerThing { 'consumer' }");
            string source = Path.Combine(root, "source");
            if (requireCaseSensitiveStorage)
            {
                string probe = Path.Combine(source, "CaseProbe");
                File.WriteAllText(probe, "probe");
                bool caseSensitive = !File.Exists(Path.Combine(source, "caseprobe"));
                File.Delete(probe);
                if (!caseSensitive)
                    return; // The same fixtures execute against real case-sensitive storage on Linux CI.
            }
            string retained = Path.Combine(source, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(retained)!);
            if (relativePath.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            {
                string rootExpression = computedProperty == "RootModule" ? computedExpression! : "'NestedRuntime.psm1'";
                File.WriteAllText(retained, "@{RootModule=" + rootExpression + ";ModuleVersion='1.0.0';RequiredModules=" + nestedRequirements +
                    (computedProperty is null || computedProperty == "RootModule" ? string.Empty : ";" + computedProperty + "=" + computedExpression) + "}");
                File.WriteAllText(Path.Combine(source, "NestedRuntime.psm1"), script);
            }
            else
                File.WriteAllText(retained, script);
            if (predecessor is not null)
                File.WriteAllText(Path.Combine(source, predecessor), "'no dependency'");
            Assert.True(ManifestEditor.TrySetTopLevelStringArray(Path.Combine(source, "source.psd1"), key,
                predecessor is null ? new[] { relativePath } : new[] { predecessor, relativePath }));
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = "source", SourcePath = source, Version = "1.0.0", KeepStaging = true },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { Merge = true, MergeMissing = true } },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "SharedHelpers", RequiredVersion = "1.0.0", VersionSource = ModuleDependencyVersionSource.Installed }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "SharedHelpers", VersionSource = ModuleDependencyVersionSource.Installed }
                    }
                }
            };
            var runner = new ModulePipelineRunner(new NullLogger(), powerShellRunner: null,
                moduleDependencyMetadataProvider: new FixtureMetadataProvider(modules),
                missingFunctionAnalysisService: new PowerShellMissingFunctionAnalysisService());
            var result = runner.Run(spec, runner.Plan(spec));
            Assert.Contains("SharedHelpers", File.ReadAllText(result.BuildResult.ManifestPath));
            Assert.True(File.Exists(Path.Combine(result.BuildResult.StagingPath, relativePath)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            WriteModule(root, "SharedHelpers", "function Get-LocalHelper { 'donor' }; Export-ModuleMember Get-LocalHelper");
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null,
                "function Get-LocalHelper { 'local' }; Get-LocalHelper",
                new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" },
                    approvedModuleSources: new[] { new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers")) },
                    requireApprovedModuleSources: true));
            Assert.Equal(new[] { "SharedHelpers" }, result.FullyInlinedApprovedModules);
            Assert.Empty(result.Functions);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Analyze_PreservesAbsentDonorWhenItsRuntimeSurfaceCannotBeInspected()
    {
        var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, "'consumer'",
            new MissingFunctionsOptions(approvedModules: new[] { "UnavailableDonor" }));
        Assert.Empty(result.FullyInlinedApprovedModules);
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
