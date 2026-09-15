using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed class ModulePipelineInstallSigningTests
{
    [Fact]
    public void SignedAutoRevisionInstall_ResignsOnlyChangedManifestBeforeDelivery()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SignedTestModule";
            var sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            WriteMinimalModule(sourceRoot.FullName, moduleName, "1.0.0");
            var installRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            Directory.CreateDirectory(Path.Combine(installRoot.FullName, moduleName, "1.0.0"));
            var hosted = new RecordingHostedOperations();
            var spec = CreateSpec(sourceRoot.FullName, moduleName, installRoot.FullName);
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: hosted);

            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            Assert.Equal(InstallationStrategy.AutoRevision, plan.InstallStrategy);
            Assert.Equal("1.0.0.1", result.InstallResult?.Version);
            var installedRoot = Assert.Single(result.InstallResult?.InstalledPaths ?? Array.Empty<string>());
            var installedManifest = Path.Combine(installedRoot, moduleName + ".psd1");
            Assert.True(ManifestEditor.TryGetTopLevelString(installedManifest, "ModuleVersion", out var installedVersion));
            Assert.Equal("1.0.0.1", installedVersion);

            var installSigningCall = Assert.Single(hosted.SigningCalls, static call => call.OverwriteSigned);
            var signedManifest = Assert.Single(installSigningCall.FilePaths);
            Assert.EndsWith(moduleName + ".psd1", signedManifest, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("1.0.0.1", installSigningCall.ManifestVersion);
            Assert.Equal(hosted.InstallManifestSha256, ComputeSha256(installedManifest));
            Assert.DoesNotContain(
                installSigningCall.FilePaths,
                static path => path.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SignedAutoRevisionInstall_DoesNotDeliverManifestWhenResigningFails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SignedTestModule";
            var sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            WriteMinimalModule(sourceRoot.FullName, moduleName, "1.0.0");

            var installRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            Directory.CreateDirectory(Path.Combine(installRoot.FullName, moduleName, "1.0.0"));
            var hosted = new RecordingHostedOperations { FailInstallManifestSigning = true };
            var spec = CreateSpec(sourceRoot.FullName, moduleName, installRoot.FullName);
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: hosted);

            var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, runner.Plan(spec)));

            Assert.Contains("install manifest signing failed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(installRoot.FullName, moduleName, "1.0.0.1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SignedAutoRevisionInstall_DoesNotOverwriteDestinationCreatedWhileSigning()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SignedTestModule";
            var sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            WriteMinimalModule(sourceRoot.FullName, moduleName, "1.0.0");

            var installRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            Directory.CreateDirectory(Path.Combine(installRoot.FullName, moduleName, "1.0.0"));
            var concurrentDestination = Path.Combine(installRoot.FullName, moduleName, "1.0.0.1");
            var sentinelPath = Path.Combine(concurrentDestination, "sentinel.txt");
            var hosted = new RecordingHostedOperations
            {
                AfterInstallManifestVersionObserved = _ =>
                {
                    Directory.CreateDirectory(concurrentDestination);
                    File.WriteAllText(sentinelPath, "concurrent install");
                }
            };
            var spec = CreateSpec(sourceRoot.FullName, moduleName, installRoot.FullName);
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: hosted);

            var exception = Assert.Throws<UnauthorizedAccessException>(() => runner.Run(spec, runner.Plan(spec)));

            Assert.Contains("will not be overwritten", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("concurrent install", File.ReadAllText(sentinelPath));
            Assert.False(File.Exists(Path.Combine(concurrentDestination, moduleName + ".psd1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SignedAutoRevisionInstall_RollsBackWhenAnyDestinationRootFails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SignedTestModule";
            var sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            WriteMinimalModule(sourceRoot.FullName, moduleName, "1.0.0");

            var firstRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "modules-first"));
            var secondRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "modules-second"));
            var firstModuleRoot = Directory.CreateDirectory(Path.Combine(firstRoot.FullName, moduleName));
            Directory.CreateDirectory(Path.Combine(firstModuleRoot.FullName, "1.0.0"));
            WriteMinimalModule(firstModuleRoot.FullName, moduleName, "0.9.0");
            Directory.CreateDirectory(Path.Combine(secondRoot.FullName, moduleName, "1.0.0"));
            File.WriteAllText(Path.Combine(secondRoot.FullName, moduleName + ".blocked"), "unrelated");
            var blockedModuleRoot = Path.Combine(secondRoot.FullName, moduleName);
            Directory.Delete(blockedModuleRoot, recursive: true);
            File.WriteAllText(blockedModuleRoot, "blocks module directory creation");

            var hosted = new RecordingHostedOperations();
            var spec = CreateSpec(sourceRoot.FullName, moduleName, firstRoot.FullName, secondRoot.FullName);
            spec.Install!.LegacyFlatHandling = LegacyFlatModuleHandling.Delete;
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: hosted);

            var exception = Assert.Throws<UnauthorizedAccessException>(() => runner.Run(spec, runner.Plan(spec)));

            Assert.Contains("every module root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(firstRoot.FullName, moduleName, "1.0.0.1")));
            Assert.True(File.Exists(Path.Combine(firstModuleRoot.FullName, moduleName + ".psd1")));
            Assert.True(File.Exists(Path.Combine(firstModuleRoot.FullName, moduleName + ".psm1")));
            Assert.True(File.Exists(blockedModuleRoot));
            Assert.Equal("blocks module directory creation", File.ReadAllText(blockedModuleRoot));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void TransactionalInstall_RollsBackWhenDeliveredValidationFails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SignedTestModule";
            var sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            WriteMinimalModule(sourceRoot.FullName, moduleName, "1.0.0.1");
            var installRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            var destination = Path.Combine(installRoot.FullName, moduleName, "1.0.0.1");
            var installer = new ModuleInstaller(new NullLogger());
            var options = new ModuleInstallerOptions(
                destinationRoots: new[] { installRoot.FullName },
                strategy: InstallationStrategy.Exact,
                keepVersions: 5,
                legacyFlatHandling: LegacyFlatModuleHandling.Ignore,
                preserveVersions: null,
                requireNewDestination: true,
                requireAllDestinationRoots: true);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                installer.InstallFromStagingTransactional(
                    sourceRoot.FullName,
                    moduleName,
                    "1.0.0.1",
                    options,
                    installedPaths =>
                    {
                        var installedPath = Assert.Single(installedPaths);
                        File.AppendAllText(Path.Combine(installedPath, moduleName + ".psd1"), "corrupted");
                        throw new InvalidOperationException("delivered byte validation failed");
                    }));

            Assert.Contains("delivered byte validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModulePipelineSpec CreateSpec(string sourceRoot, string moduleName, params string[] installRoots)
        => new()
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = sourceRoot,
                Version = "1.0.0",
                ExcludeDirectories = Array.Empty<string>(),
                ExcludeFiles = Array.Empty<string>()
            },
            Install = new ModulePipelineInstallOptions
            {
                Enabled = true,
                Strategy = InstallationStrategy.AutoRevision,
                KeepVersions = 5,
                Roots = installRoots,
                UpdateManifestToResolvedVersion = true
            },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration { SignMerged = true }
                },
                new ConfigurationOptionsSegment
                {
                    Options = new ConfigurationOptions
                    {
                        Signing = new SigningOptionsConfiguration { CertificateThumbprint = "AABBCC" }
                    }
                }
            }
        };

    private static void WriteMinimalModule(string root, string moduleName, string version)
    {
        File.WriteAllText(
            Path.Combine(root, moduleName + ".psd1"),
            $"@{{ ModuleVersion = '{version}'; RootModule = '{moduleName}.psm1'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }}");
        File.WriteAllText(Path.Combine(root, moduleName + ".psm1"), string.Empty);
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class RecordingHostedOperations : IModulePipelineHostedOperations
    {
        public List<SigningCall> SigningCalls { get; } = new();
        public bool FailInstallManifestSigning { get; set; }
        public Action<string>? AfterInstallManifestVersionObserved { get; set; }
        public string? InstallManifestSha256 { get; private set; }

        public ModuleSigningResult SignModuleOutput(
            string moduleName,
            string rootPath,
            string[] packageFilePaths,
            string[] includePatterns,
            string[] excludeSubstrings,
            SigningOptionsConfiguration signing)
        {
            var paths = packageFilePaths.Select(Path.GetFullPath).ToArray();
            string? manifestVersion = null;
            if (signing.OverwriteSigned == true && paths.Length == 1 &&
                paths[0].EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(ManifestEditor.TryGetTopLevelString(paths[0], "ModuleVersion", out manifestVersion));
            }

            SigningCalls.Add(new SigningCall(paths, signing.OverwriteSigned == true, manifestVersion));
            if (manifestVersion is not null)
            {
                if (FailInstallManifestSigning)
                    throw new InvalidOperationException("install manifest signing failed");

                AfterInstallManifestVersionObserved?.Invoke(manifestVersion);
                File.AppendAllText(paths[0], Environment.NewLine + "# test install signature");
                InstallManifestSha256 = ComputeSha256(paths[0]);
            }

            return new ModuleSigningResult
            {
                TotalMatched = paths.Length,
                TotalAfterExclude = paths.Length,
                Attempted = paths.Length,
                SignedNew = manifestVersion is null ? paths.Length : 0,
                Resigned = manifestVersion is null ? 0 : paths.Length,
                CertificateThumbprint = "AABBCC",
                VerifiedFilePaths = paths
            };
        }

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
            => throw new InvalidOperationException("Not used in this test.");

        public ModulePublishResult PublishModule(
            PublishConfiguration publish,
            ModulePipelinePlan plan,
            ModuleBuildResult buildResult,
            IReadOnlyList<ArtefactBuildResult> artefactResults,
            bool includeScriptFolders,
            Action? remotePublishAttempted,
            Action? remoteSideEffectObserved,
            Action<string, string>? finalizeRepositoryModule,
            IGitHubReleaseProgressReporter? gitHubProgress)
            => throw new InvalidOperationException("Not used in this test.");

        public ModulePipelineActionResult RunAction(
            ModulePipelineActionConfiguration action,
            ModulePipelineActionContext context,
            string contextPath,
            string projectRoot)
            => throw new InvalidOperationException("Not used in this test.");

        public void ValidateModuleImports(
            string manifestPath,
            ImportModuleEntry[] modules,
            bool importRequired,
            bool importSelf,
            bool verbose,
            ModuleImportValidationTarget[] targets)
            => throw new InvalidOperationException("Not used in this test.");
    }

    private sealed record SigningCall(string[] FilePaths, bool OverwriteSigned, string? ManifestVersion);
}
