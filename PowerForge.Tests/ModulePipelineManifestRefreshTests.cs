using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineManifestRefreshTests
{
    [Fact]
    public void RefreshProjectManifestFromPlan_UsesInjectedManifestMutator()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteModuleWithStaleManifest(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "3.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var manifestMutator = new RecordingManifestMutator();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeDependencyMetadataProvider(),
                new FakeHostedOperations(),
                manifestMutator);

            var plan = runner.Plan(spec);
            var method = typeof(ModulePipelineRunner).GetMethod("RefreshProjectManifestFromPlan", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(method is not null, "RefreshProjectManifestFromPlan method signature may have changed.");

            method!.Invoke(runner, new object?[] { plan, Path.Combine(root.FullName, $"{moduleName}.psd1") });

            Assert.Contains(manifestMutator.TopLevelVersionWrites, static write => write.NewVersion == "3.0.0");
            Assert.Contains(manifestMutator.TopLevelStringWrites, static write => write.Key == "RootModule" && write.Value == "TestModule.psm1");
            Assert.Contains(manifestMutator.RequiredModulesWrites, static write => write.Modules.Length == 1 && write.Modules[0].ModuleName == "LegacyOnly");
            Assert.Contains(manifestMutator.RemovedTopLevelKeys, static key => string.Equals(key, "CommandModuleDependencies", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RefreshesManifestMetadataAndClearsStaleValues()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteModuleWithStaleManifest(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "3.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "3.0.0",
                            CompatiblePSEditions = new[] { "Desktop", "Core" },
                            Guid = "22222222-2222-2222-2222-222222222222",
                            Author = "New Author",
                            CompanyName = null,
                            Copyright = null,
                            Description = "Fresh description",
                            PowerShellVersion = "5.1",
                            Tags = null,
                            IconUri = null,
                            ProjectUri = "https://new.example/project",
                            DotNetFrameworkVersion = null,
                            LicenseUri = null,
                            RequireLicenseAcceptance = false,
                            Prerelease = null,
                            FormatsToProcess = null
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);
            var manifestPath = result.BuildResult.ManifestPath;

            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "GUID", out var guid));
            Assert.Equal("22222222-2222-2222-2222-222222222222", guid);
            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "ModuleVersion", out var version));
            Assert.Equal("3.0.0", version);
            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "Author", out var author));
            Assert.Equal("New Author", author);
            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "Description", out var description));
            Assert.Equal("Fresh description", description);
            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "PowerShellVersion", out var psVersion));
            Assert.Equal("5.1", psVersion);

            Assert.False(ManifestEditor.TryGetTopLevelString(manifestPath, "CompanyName", out _));
            Assert.False(ManifestEditor.TryGetTopLevelString(manifestPath, "Copyright", out _));
            Assert.False(ManifestEditor.TryGetTopLevelString(manifestPath, "DotNetFrameworkVersion", out _));
            Assert.False(ManifestEditor.TryGetTopLevelString(manifestPath, "Prerelease", out _));
            Assert.False(ManifestEditor.TryGetTopLevelStringArray(manifestPath, "FormatsToProcess", out _));
            Assert.False(ManifestEditor.TryGetPsDataStringArray(manifestPath, "Tags", out _));

            Assert.True(ManifestEditor.TryGetRequiredModules(manifestPath, out RequiredModuleReference[]? requiredModules));
            var required = Assert.Single(requiredModules!);
            Assert.Equal("LegacyOnly", required.ModuleName);
            Assert.True(ManifestEditor.TryGetPsDataStringArray(manifestPath, "ExternalModuleDependencies", out var externalModules));
            Assert.Equal(new[] { "Old.External" }, externalModules);

            var content = File.ReadAllText(manifestPath);
            Assert.DoesNotContain("CommandModuleDependencies", content, StringComparison.Ordinal);
            Assert.Contains("ProjectUri = 'https://new.example/project'", content, StringComparison.Ordinal);
            Assert.DoesNotContain("IconUri =", content, StringComparison.Ordinal);
            Assert.DoesNotContain("LicenseUri =", content, StringComparison.Ordinal);
            Assert.Contains("RequireLicenseAcceptance = $false", content, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_NormalizesScriptsToProcessLayoutWithoutChangingValue()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            File.WriteAllText(Path.Combine(root.FullName, $"{moduleName}.psm1"), "function Test-Example { 'ok' }");
            File.WriteAllText(Path.Combine(root.FullName, $"{moduleName}.psd1"),
                "@{" + Environment.NewLine +
                $"    RootModule = '{moduleName}.psm1'" + Environment.NewLine +
                "    ModuleVersion = '1.0.0'" + Environment.NewLine +
                Environment.NewLine +
                "    ScriptsToProcess = @('Init.ps1')" + Environment.NewLine +
                "}" + Environment.NewLine);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);
            var manifestPath = result.BuildResult.ManifestPath;

            Assert.True(ManifestEditor.TryGetTopLevelStringArray(manifestPath, "ScriptsToProcess", out var scripts));
            Assert.NotNull(scripts);
            Assert.Equal(new[] { "Init.ps1" }, scripts);

            var content = File.ReadAllText(manifestPath).Replace("\r\n", "\n");
            Assert.Matches(new Regex(@"ScriptsToProcess\s*=\s*@\('Init\.ps1'\)", RegexOptions.CultureInvariant), content);
            Assert.DoesNotMatch(new Regex(@"\n\n\s*ScriptsToProcess\s*=", RegexOptions.CultureInvariant), content);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_WritesPrereleaseToPsDataAndRemovesTopLevelPrerelease()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteModuleWithStaleManifest(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "3.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "3.0.0",
                            Guid = "22222222-2222-2222-2222-222222222222",
                            Author = "New Author",
                            Prerelease = "preview2"
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);
            var manifestPath = result.BuildResult.ManifestPath;

            Assert.False(ManifestEditor.TryGetTopLevelString(manifestPath, "Prerelease", out _));
            Assert.True(ManifestEditor.TryGetPsDataStringArray(manifestPath, "Prerelease", out var prerelease));
            Assert.NotNull(prerelease);
            Assert.Equal(new[] { "preview2" }, prerelease);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RefreshesProjectManifestBeforeTestsAfterMergeCanFail()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteModuleWithStaleManifest(root.FullName, moduleName, "1.0.0");
            var testsPath = Directory.CreateDirectory(Path.Combine(root.FullName, "Tests")).FullName;

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "3.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "3.0.0",
                            Guid = "22222222-2222-2222-2222-222222222222",
                            Author = "New Author",
                            Prerelease = null
                        }
                    },
                    new ConfigurationTestSegment
                    {
                        Configuration = new TestConfiguration
                        {
                            TestsPath = testsPath,
                            Force = false
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeDependencyMetadataProvider(),
                new FakeHostedOperations
                {
                    NextTestSuiteResult = new ModuleTestSuiteResult(
                        projectPath: root.FullName,
                        testPath: testsPath,
                        moduleName: moduleName,
                        moduleVersion: "3.0.0",
                        manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                        requiredModules: Array.Empty<RequiredModuleReference>(),
                        dependencyResults: Array.Empty<ModuleDependencyInstallResult>(),
                        moduleImported: true,
                        exportedFunctionCount: null,
                        exportedCmdletCount: null,
                        exportedAliasCount: null,
                        pesterVersion: "5.7.1",
                        totalCount: 2,
                        passedCount: 1,
                        failedCount: 1,
                        skippedCount: 0,
                        duration: null,
                        coveragePercent: null,
                        failureAnalysis: new ModuleTestFailureAnalysis
                        {
                            Source = "PesterResults",
                            Timestamp = DateTime.Now,
                            TotalCount = 2,
                            PassedCount = 1,
                            FailedCount = 1,
                            FailedTests = new[]
                            {
                                new ModuleTestFailureInfo
                                {
                                    Name = "Broken.Test",
                                    ErrorMessage = "boom"
                                }
                            }
                        },
                        exitCode: 1,
                        stdOut: string.Empty,
                        stdErr: string.Empty,
                        resultsXmlPath: null)
                });

            var plan = runner.Plan(spec);
            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));
            Assert.Contains("Broken.Test", ex.Message, StringComparison.Ordinal);

            var projectManifestPath = Path.Combine(root.FullName, $"{moduleName}.psd1");
            Assert.False(ManifestEditor.TryGetTopLevelString(projectManifestPath, "Prerelease", out _));
            Assert.False(ManifestEditor.TryGetPsDataStringArray(projectManifestPath, "Prerelease", out _));
            Assert.True(ManifestEditor.TryGetTopLevelString(projectManifestPath, "ModuleVersion", out var version));
            Assert.Equal("3.0.0", version);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RemovesInboxAndDuplicatedExternalDependenciesFromManifest()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteModuleWithStaleDependencyMetadata(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);
            var manifestPath = result.BuildResult.ManifestPath;

            Assert.True(ManifestEditor.TryGetRequiredModules(manifestPath, out RequiredModuleReference[]? requiredModules));
            Assert.NotNull(requiredModules);
            Assert.Single(requiredModules!);
            Assert.Contains(requiredModules, module => string.Equals(module.ModuleName, "LegacyOnly", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(requiredModules, module => string.Equals(module.ModuleName, "Microsoft.PowerShell.Utility", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(requiredModules, module => string.Equals(module.ModuleName, "Az.Accounts", StringComparison.OrdinalIgnoreCase));

            Assert.True(ManifestEditor.TryGetPsDataStringArray(manifestPath, "ExternalModuleDependencies", out var externalModules));
            Assert.Equal(new[] { "Az.Accounts" }, externalModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteModuleWithStaleManifest(string rootPath, string moduleName, string version)
    {
        File.WriteAllText(Path.Combine(rootPath, $"{moduleName}.psm1"), "function Test-Example { 'ok' }");
        File.WriteAllText(Path.Combine(rootPath, $"{moduleName}.psd1"),
            "@{" + Environment.NewLine +
            $"    RootModule = '{moduleName}.psm1'" + Environment.NewLine +
            $"    ModuleVersion = '{version}'" + Environment.NewLine +
            "    GUID = '11111111-1111-1111-1111-111111111111'" + Environment.NewLine +
            "    Author = 'Old Author'" + Environment.NewLine +
            "    CompanyName = 'Old Company'" + Environment.NewLine +
            "    Copyright = 'Old Copyright'" + Environment.NewLine +
            "    Description = 'Old description'" + Environment.NewLine +
            "    PowerShellVersion = '2.0'" + Environment.NewLine +
            "    DotNetFrameworkVersion = '4.0'" + Environment.NewLine +
            "    Prerelease = 'preview1'" + Environment.NewLine +
            "    CommandModuleDependencies = @{ 'Old.Module' = @('Get-Old') }" + Environment.NewLine +
            "    FormatsToProcess = @('Old.format.ps1xml')" + Environment.NewLine +
            "    RequiredModules = @('LegacyOnly')" + Environment.NewLine +
            "    FunctionsToExport = @('Test-Example')" + Environment.NewLine +
            "    CmdletsToExport = @()" + Environment.NewLine +
            "    AliasesToExport = @()" + Environment.NewLine +
            "    PrivateData = @{" + Environment.NewLine +
            "        PSData = @{" + Environment.NewLine +
            "            Tags = @('OldTag')" + Environment.NewLine +
            "            IconUri = 'https://old.example/icon.png'" + Environment.NewLine +
            "            ProjectUri = 'https://old.example/project'" + Environment.NewLine +
            "            LicenseUri = 'https://old.example/license'" + Environment.NewLine +
            "            RequireLicenseAcceptance = $true" + Environment.NewLine +
            "            ExternalModuleDependencies = @('Old.External')" + Environment.NewLine +
            "        }" + Environment.NewLine +
            "    }" + Environment.NewLine +
            "}" + Environment.NewLine);
    }

    private static void WriteModuleWithStaleDependencyMetadata(string rootPath, string moduleName, string version)
    {
        File.WriteAllText(Path.Combine(rootPath, $"{moduleName}.psm1"), "function Test-Example { 'ok' }");
        File.WriteAllText(Path.Combine(rootPath, $"{moduleName}.psd1"),
            "@{" + Environment.NewLine +
            $"    RootModule = '{moduleName}.psm1'" + Environment.NewLine +
            $"    ModuleVersion = '{version}'" + Environment.NewLine +
            "    GUID = '11111111-1111-1111-1111-111111111111'" + Environment.NewLine +
            "    Author = 'Old Author'" + Environment.NewLine +
            "    Description = 'Old description'" + Environment.NewLine +
            "    RequiredModules = @('LegacyOnly', 'Microsoft.PowerShell.Utility', 'Az.Accounts')" + Environment.NewLine +
            "    FunctionsToExport = @('Test-Example')" + Environment.NewLine +
            "    CmdletsToExport = @()" + Environment.NewLine +
            "    AliasesToExport = @()" + Environment.NewLine +
            "    PrivateData = @{" + Environment.NewLine +
            "        PSData = @{" + Environment.NewLine +
            "            ExternalModuleDependencies = @('Microsoft.PowerShell.Utility', 'Az.Accounts')" + Environment.NewLine +
            "        }" + Environment.NewLine +
            "    }" + Environment.NewLine +
            "}" + Environment.NewLine);
    }

    private sealed class RecordingManifestMutator : IModuleManifestMutator
    {
        public List<(string FilePath, string NewVersion)> TopLevelVersionWrites { get; } = new();
        public List<(string FilePath, string Key, string Value)> TopLevelStringWrites { get; } = new();
        public List<(string FilePath, RequiredModuleReference[] Modules)> RequiredModulesWrites { get; } = new();
        public List<string> RemovedTopLevelKeys { get; } = new();

        public bool TrySetTopLevelModuleVersion(string filePath, string newVersion)
        {
            TopLevelVersionWrites.Add((filePath, newVersion));
            return true;
        }

        public bool TrySetTopLevelString(string filePath, string key, string newValue)
        {
            TopLevelStringWrites.Add((filePath, key, newValue));
            return true;
        }

        public bool TrySetTopLevelStringArray(string filePath, string key, string[] values) => true;
        public bool TrySetPsDataString(string filePath, string key, string value) => true;
        public bool TrySetPsDataStringArray(string filePath, string key, string[] values) => true;
        public bool TrySetPsDataBool(string filePath, string key, bool value) => true;

        public bool TryRemoveTopLevelKey(string filePath, string key)
        {
            RemovedTopLevelKeys.Add(key);
            return true;
        }

        public bool TryRemovePsDataKey(string filePath, string key) => true;

        public bool TrySetRequiredModules(string filePath, RequiredModuleReference[] modules)
        {
            RequiredModulesWrites.Add((filePath, modules ?? Array.Empty<RequiredModuleReference>()));
            return true;
        }

        public bool TrySetPsDataSubString(string filePath, string parentKey, string key, string value) => true;
        public bool TrySetPsDataSubStringArray(string filePath, string parentKey, string key, string[] values) => true;
        public bool TrySetPsDataSubBool(string filePath, string parentKey, string key, bool value) => true;
        public bool TrySetPsDataSubHashtableArray(string filePath, string parentKey, string key, IReadOnlyList<IReadOnlyDictionary<string, string>> values) => true;
        public bool TrySetManifestExports(string filePath, string[]? functions, string[]? cmdlets, string[]? aliases) => true;
        public bool TrySetRepository(string filePath, string? branch, string[]? paths) => true;
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell runner should not be used in this test.");
    }

    private sealed class FakeDependencyMetadataProvider : IModuleDependencyMetadataProvider
    {
        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<string>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakeHostedOperations : IModulePipelineHostedOperations
    {
        public ModuleTestSuiteResult? NextTestSuiteResult { get; set; }

        public IReadOnlyList<ModuleDependencyInstallResult> EnsureDependenciesInstalled(
            ModuleDependency[] dependencies,
            ModuleSkipConfiguration? skipModules,
            bool force,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => Array.Empty<ModuleDependencyInstallResult>();

        public DocumentationBuildResult BuildDocumentation(
            string moduleName,
            string stagingPath,
            string moduleManifestPath,
            DocumentationConfiguration documentation,
            BuildDocumentationConfiguration buildDocumentation,
            IModulePipelineProgressReporter progress,
            ModulePipelineStep? extractStep,
            ModulePipelineStep? writeStep,
            ModulePipelineStep? externalHelpStep)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleValidationReport ValidateModule(ModuleValidationSpec spec)
            => throw new InvalidOperationException("Not used in this test.");

        public void EnsureBinaryDependenciesValid(string moduleRoot, string powerShellEdition, string? modulePath, string? validationTarget)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleTestSuiteResult RunModuleTestSuite(ModuleTestSuiteSpec spec)
            => NextTestSuiteResult ?? throw new InvalidOperationException("Not used in this test.");

        public ModulePublishResult PublishModule(
            PublishConfiguration publish,
            ModulePipelinePlan plan,
            ModuleBuildResult buildResult,
            IReadOnlyList<ArtefactBuildResult> artefactResults,
            bool includeScriptFolders)
            => throw new InvalidOperationException("Not used in this test.");

        public void ValidateModuleImports(
            string manifestPath,
            ImportModuleEntry[] modules,
            bool importRequired,
            bool importSelf,
            bool verbose,
            ModuleImportValidationTarget[] targets)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleSigningResult SignModuleOutput(
            string moduleName,
            string rootPath,
            string[] includePatterns,
            string[] excludeSubstrings,
            SigningOptionsConfiguration signing)
            => throw new InvalidOperationException("Not used in this test.");
    }
}
