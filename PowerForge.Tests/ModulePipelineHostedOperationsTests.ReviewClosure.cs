using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineHostedOperationsTests
{
    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_SkipsMissingInstalledOnlyDependencyBeforeMetadataValidation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var spec = CreateDependencySpec(
                root.FullName,
                moduleName,
                new ConfigurationModuleSkipSegment
                {
                    Configuration = new ModuleSkipConfiguration { IgnoreModuleName = new[] { "Dependency.Tools" } }
                },
                CreateModuleSegment(ModuleDependencyKind.RequiredModule, "1.0.0", ModuleDependencyVersionSource.Installed));
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, runner.Plan(spec));

            var skipped = Assert.Single(result);
            Assert.Equal(ModuleDependencyInstallStatus.Skipped, skipped.Status);
            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_DoesNotSendFallbackCredentialToPSGallery()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var fallbackCredential = new RepositoryCredential { UserName = "private-user", Secret = "private-secret" };
            var dependency = CreateModuleSegment(ModuleDependencyKind.RequiredModule, "1.0.0", ModuleDependencyVersionSource.PSGallery);
            var spec = CreateDependencySpec(root.FullName, moduleName, dependency);
            Assert.IsType<ConfigurationBuildSegment>(spec.Segments[0]).BuildModule!.InstallMissingModulesCredential = fallbackCredential;
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider("Microsoft.PowerShell.PSResourceGet"),
                hostedOperations);

            InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, runner.Plan(spec));

            Assert.Equal("PSGallery", hostedOperations.LastRepository);
            Assert.Null(hostedOperations.LastCredential);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_ValidatesEachSameNamedInstalledConstraintIndependently()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var spec = CreateDependencySpec(
                root.FullName,
                moduleName,
                CreateModuleSegment(ModuleDependencyKind.RequiredModule, "1.0.0", ModuleDependencyVersionSource.Installed),
                CreateModuleSegment(ModuleDependencyKind.EmbeddedModule, "2.0.0", ModuleDependencyVersionSource.Installed));
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata("Dependency.Tools", "1.0.0", null, Path.Combine(root.FullName, "Dependency.Tools")));
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                provider,
                new FakeHostedOperations());

            var exception = Assert.Throws<TargetInvocationException>(
                () => InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, runner.Plan(spec)));

            var failure = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("no installed version satisfies", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, provider.Requests.Count);
            Assert.All(provider.Requests, request => Assert.Single(request));
            Assert.Equal("1.0.0", provider.Requests[0][0].RequiredVersion);
            Assert.Equal("2.0.0", provider.Requests[1][0].RequiredVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_RejectsConflictingSourcesForEquivalentDeclarations()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var spec = CreateDependencySpec(
                root.FullName,
                moduleName,
                CreateModuleSegment(ModuleDependencyKind.RequiredModule, "1.0.0", ModuleDependencyVersionSource.PSGallery),
                CreateModuleSegment(ModuleDependencyKind.EmbeddedModule, "1.0.0", ModuleDependencyVersionSource.Installed));
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                new FakeHostedOperations());

            var exception = Assert.Throws<TargetInvocationException>(
                () => InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, runner.Plan(spec)));

            Assert.Contains("Conflicting dependency source policies", Assert.IsType<InvalidOperationException>(exception.InnerException).Message);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_RejectsRepositoryResultWithWrongGuid()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string expectedGuid = "11111111-1111-1111-1111-111111111111";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var dependency = CreateModuleSegment(ModuleDependencyKind.RequiredModule, "1.0.0", ModuleDependencyVersionSource.PSGallery);
            dependency.Configuration!.Guid = expectedGuid;
            var spec = CreateDependencySpec(root.FullName, moduleName, dependency);
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata(
                    "Dependency.Tools",
                    "1.0.0",
                    "22222222-2222-2222-2222-222222222222",
                    Path.Combine(root.FullName, "Dependency.Tools")));
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                provider,
                new FakeHostedOperations());

            var exception = Assert.Throws<TargetInvocationException>(
                () => InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, runner.Plan(spec)));

            Assert.Contains("declared GUID", Assert.IsType<InvalidOperationException>(exception.InnerException).Message);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("Latest")]
    public void Plan_ResolvesExternalInstalledVersionTokensBeforeValidation(string token)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var dependency = new ConfigurationModuleSegment
            {
                Kind = ModuleDependencyKind.ExternalModule,
                Configuration = new ModuleDependencyConfiguration
                {
                    ModuleName = "Dependency.Tools",
                    ModuleVersion = token,
                    VersionSource = ModuleDependencyVersionSource.Installed
                }
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Dependency.Tools"] = "2.5.0"
                }),
                new FakeHostedOperations());

            var source = Assert.Single(runner.Plan(CreateDependencySpec(root.FullName, moduleName, dependency)).DependencySourceResolutions);

            Assert.Equal("2.5.0", source.MinimumVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModulePipelineSpec CreateDependencySpec(
        string root,
        string moduleName,
        params IConfigurationSegment[] dependencySegments)
    {
        return new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = root,
                Version = "1.0.0",
                CsprojPath = null
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration { InstallMissingModules = true }
                }
            }.Concat(dependencySegments).ToArray()
        };
    }

    private static ConfigurationModuleSegment CreateModuleSegment(
        ModuleDependencyKind kind,
        string requiredVersion,
        ModuleDependencyVersionSource versionSource)
        => new()
        {
            Kind = kind,
            Configuration = new ModuleDependencyConfiguration
            {
                ModuleName = "Dependency.Tools",
                RequiredVersion = requiredVersion,
                VersionSource = versionSource
            }
        };

    private sealed class FixedVersionedMetadataProvider : IModuleDependencyVersionedMetadataProvider
    {
        private readonly InstalledModuleMetadata _installed;

        internal List<RequiredModuleReference[]> Requests { get; } = new();

        internal FixedVersionedMetadataProvider(InstalledModuleMetadata installed)
        {
            _installed = installed;
        }

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetInstalledModules(IReadOnlyList<RequiredModuleReference> references)
        {
            Requests.Add(references?.ToArray() ?? Array.Empty<RequiredModuleReference>());
            return new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                [_installed.Name] = _installed
            };
        }

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                [_installed.Name] = _installed
            };

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<RequiredModuleReference>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
    }
}
