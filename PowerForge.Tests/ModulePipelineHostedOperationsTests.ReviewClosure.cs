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
    public void EnsureBuildDependenciesInstalledIfNeeded_ValidatesSameNamedExternalConstraintIndependently()
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
                CreateModuleSegment(ModuleDependencyKind.ExternalModule, "2.0.0", ModuleDependencyVersionSource.Installed));
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
    public void EnsureBuildDependenciesInstalledIfNeeded_DeduplicatesEquivalentSameNamedExternalConstraint()
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
                CreateModuleSegment(ModuleDependencyKind.ExternalModule, "1.0.0", ModuleDependencyVersionSource.Installed));
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata("Dependency.Tools", "1.0.0", null, Path.Combine(root.FullName, "Dependency.Tools")));
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                provider,
                new FakeHostedOperations());

            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, runner.Plan(spec));

            Assert.Single(result);
            Assert.Single(provider.Requests);
            Assert.Single(provider.Requests[0]);
            Assert.Equal("1.0.0", provider.Requests[0][0].RequiredVersion);
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

    [Theory]
    [InlineData(ModuleDependencyKind.RequiredModule)]
    [InlineData(ModuleDependencyKind.EmbeddedModule)]
    [InlineData(ModuleDependencyKind.ExternalModule)]
    public void EnsureBuildDependenciesInstalledIfNeeded_MatchesAutoResolvedInstalledPrereleaseByBaseVersion(
        ModuleDependencyKind dependencyKind)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var dependency = CreateModuleSegment(
                dependencyKind,
                "Auto",
                ModuleDependencyVersionSource.Installed);
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata(
                    "Dependency.Tools",
                    "2.0.0-beta.1",
                    null,
                    Path.Combine(root.FullName, "Dependency.Tools", "2.0.0")));
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                provider,
                new FakeHostedOperations());

            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(
                runner,
                runner.Plan(CreateDependencySpec(root.FullName, moduleName, dependency)));

            Assert.Single(result);
            var request = Assert.Single(provider.Requests.SelectMany(static item => item));
            Assert.True(Assert.IsType<ModuleInstalledReference>(request).MatchPrereleaseByBaseVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(ModuleDependencyKind.RequiredModule)]
    [InlineData(ModuleDependencyKind.EmbeddedModule)]
    [InlineData(ModuleDependencyKind.ExternalModule)]
    public void EnsureBuildDependenciesInstalledIfNeeded_UsesResolvedPrereleaseIdentityForRepositoryInstall(
        ModuleDependencyKind dependencyKind)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string resolvedPrerelease = "2.0.0-beta.1";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var dependency = CreateModuleSegment(
                dependencyKind,
                "Auto",
                ModuleDependencyVersionSource.PSGallery);
            var spec = CreateDependencySpec(root.FullName, moduleName, dependency);
            var build = Assert.IsType<ConfigurationBuildSegment>(spec.Segments[0]);
            build.BuildModule!.InstallMissingModulesPrerelease = true;
            var hostedOperations = new FakeHostedOperations();
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata(
                    "Dependency.Tools",
                    "1.0.0",
                    null,
                    Path.Combine(root.FullName, "Dependency.Tools", "1.0.0")),
                latest: new InstalledModuleMetadata(
                    "Dependency.Tools",
                    "2.0.0",
                    null,
                    Path.Combine(root.FullName, "Dependency.Tools", "2.0.0")),
                onlineVersion: resolvedPrerelease);
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new RecordingPowerShellRunner(_ => new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh")),
                provider,
                hostedOperations);

            var plan = runner.Plan(spec);
            InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            var manifestReference = dependencyKind switch
            {
                ModuleDependencyKind.RequiredModule => Assert.Single(plan.RequiredModules),
                ModuleDependencyKind.EmbeddedModule => Assert.Single(plan.EmbeddedModules),
                ModuleDependencyKind.ExternalModule => null,
                _ => throw new InvalidOperationException($"Unsupported dependency kind: {dependencyKind}")
            };
            if (manifestReference is not null)
                Assert.Equal("2.0.0", manifestReference.RequiredVersion);

            var installRequest = Assert.Single(hostedOperations.LastDependencies);
            Assert.Equal(resolvedPrerelease, installRequest.RequiredVersion);
            Assert.Null(installRequest.MinimumVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_ValidatesGuidAgainstResolvedPrereleaseIdentity()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string expectedGuid = "11111111-1111-1111-1111-111111111111";
            const string resolvedPrerelease = "2.0.0-beta.1";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var dependency = CreateModuleSegment(ModuleDependencyKind.RequiredModule, "Auto", ModuleDependencyVersionSource.PSGallery);
            dependency.Configuration!.Guid = expectedGuid;
            var spec = CreateDependencySpec(root.FullName, moduleName, dependency);
            Assert.IsType<ConfigurationBuildSegment>(spec.Segments[0]).BuildModule!.InstallMissingModulesPrerelease = true;
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata("Dependency.Tools", resolvedPrerelease, expectedGuid, Path.Combine(root.FullName, "Dependency.Tools")),
                latest: new InstalledModuleMetadata("Dependency.Tools", "1.0.0", expectedGuid, Path.Combine(root.FullName, "Dependency.Tools")),
                onlineVersion: resolvedPrerelease);
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new RecordingPowerShellRunner(_ => new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh")),
                provider,
                new FakeHostedOperations());

            InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, runner.Plan(spec));

            var validationRequest = Assert.Single(provider.Requests.SelectMany(static request => request));
            Assert.Equal(resolvedPrerelease, validationRequest.RequiredVersion);
            Assert.Equal(expectedGuid, validationRequest.Guid);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnsureBuildDependenciesInstalledIfNeeded_PreservesExplicitPrereleaseConstraintOutsideManifest(bool exact)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string prerelease = "2.0.0-beta.1";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var dependency = new ConfigurationModuleSegment
            {
                Kind = ModuleDependencyKind.RequiredModule,
                Configuration = new ModuleDependencyConfiguration
                {
                    ModuleName = "Dependency.Tools",
                    RequiredVersion = exact ? prerelease : null,
                    ModuleVersion = exact ? null : prerelease,
                    VersionSource = ModuleDependencyVersionSource.PSGallery
                }
            };
            var spec = CreateDependencySpec(root.FullName, moduleName, dependency);
            Assert.IsType<ConfigurationBuildSegment>(spec.Segments[0]).BuildModule!.InstallMissingModulesPrerelease = true;
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new RecordingPowerShellRunner(_ => new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh")),
                new FakeMetadataProvider("Microsoft.PowerShell.PSResourceGet"),
                hostedOperations);

            var plan = runner.Plan(spec);
            InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            var manifestReference = Assert.Single(plan.RequiredModules);
            var installRequest = Assert.Single(hostedOperations.LastDependencies);
            if (exact)
            {
                Assert.Equal("2.0.0", manifestReference.RequiredVersion);
                Assert.Equal(prerelease, installRequest.RequiredVersion);
                Assert.Null(installRequest.MinimumVersion);
            }
            else
            {
                Assert.Equal("2.0.0", manifestReference.ModuleVersion);
                Assert.Null(installRequest.RequiredVersion);
                Assert.Equal(prerelease, installRequest.MinimumVersion);
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_RejectsConflictingResolvedIdentitiesAcrossDependencyKinds()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var required = CreateModuleSegment(ModuleDependencyKind.RequiredModule, "Auto", ModuleDependencyVersionSource.PSGallery);
            var external = CreateModuleSegment(ModuleDependencyKind.ExternalModule, "2.0.0", ModuleDependencyVersionSource.PSGallery);
            var spec = CreateDependencySpec(root.FullName, moduleName, required, external);
            Assert.IsType<ConfigurationBuildSegment>(spec.Segments[0]).BuildModule!.InstallMissingModulesPrerelease = true;
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata("Dependency.Tools", "1.0.0", null, Path.Combine(root.FullName, "Dependency.Tools")),
                onlineVersion: "2.0.0-beta.1");
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new RecordingPowerShellRunner(_ => new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh")),
                provider,
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
    public void EnsureBuildDependenciesInstalledIfNeeded_PreservesDifferentResolvedBaseVersionsAcrossDependencyKinds()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var required = CreateModuleSegment(ModuleDependencyKind.RequiredModule, "2.0.0-beta.1", ModuleDependencyVersionSource.PSGallery);
            var external = CreateModuleSegment(ModuleDependencyKind.ExternalModule, "3.0.0-beta.1", ModuleDependencyVersionSource.PSGallery);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new RecordingPowerShellRunner(_ => new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh")),
                new FakeMetadataProvider("Microsoft.PowerShell.PSResourceGet"),
                hostedOperations);

            InvokeEnsureBuildDependenciesInstalledIfNeeded(
                runner,
                runner.Plan(CreateDependencySpec(root.FullName, moduleName, required, external)));

            Assert.Equal(
                new[] { "2.0.0-beta.1", "3.0.0-beta.1" },
                hostedOperations.LastDependencies.Select(static dependency => dependency.RequiredVersion).ToArray());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_RejectsInstalledPrereleaseDifferentFromAutoResolvedIdentity()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var dependency = CreateModuleSegment(ModuleDependencyKind.RequiredModule, "Auto", ModuleDependencyVersionSource.Installed);
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata("Dependency.Tools", "2.0.0-beta.2", null, Path.Combine(root.FullName, "Dependency.Tools", "2.0.0")),
                latest: new InstalledModuleMetadata("Dependency.Tools", "2.0.0-beta.1", null, Path.Combine(root.FullName, "Dependency.Tools", "2.0.0")));
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                provider,
                new FakeHostedOperations());

            var exception = Assert.Throws<TargetInvocationException>(() =>
                InvokeEnsureBuildDependenciesInstalledIfNeeded(
                    runner,
                    runner.Plan(CreateDependencySpec(root.FullName, moduleName, dependency))));

            Assert.Contains("no installed version satisfies", Assert.IsType<InvalidOperationException>(exception.InnerException).Message, StringComparison.OrdinalIgnoreCase);
            var request = Assert.IsType<ModuleInstalledReference>(Assert.Single(provider.Requests.SelectMany(static item => item)));
            Assert.Equal("2.0.0-beta.1", request.ResolvedVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_RejectsStableInstalledModuleForExplicitPrerelease()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var dependency = CreateModuleSegment(ModuleDependencyKind.RequiredModule, "2.0.0-beta.1", ModuleDependencyVersionSource.Installed);
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata("Dependency.Tools", "2.0.0", null, Path.Combine(root.FullName, "Dependency.Tools", "2.0.0")));
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                provider,
                new FakeHostedOperations());

            var exception = Assert.Throws<TargetInvocationException>(() =>
                InvokeEnsureBuildDependenciesInstalledIfNeeded(
                    runner,
                    runner.Plan(CreateDependencySpec(root.FullName, moduleName, dependency))));

            Assert.Contains("no installed version satisfies", Assert.IsType<InvalidOperationException>(exception.InnerException).Message, StringComparison.OrdinalIgnoreCase);
            var request = Assert.IsType<ResolvedRequiredModuleReference>(Assert.Single(provider.Requests.SelectMany(static item => item)));
            Assert.Equal("2.0.0-beta.1", request.ResolvedVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_UsesResolvedPrereleaseIdentityForTransitiveRepositoryInstall()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string rootDependency = "Root.Tools";
            const string childDependency = "Child.Tools";
            const string childPrerelease = "2.0.0-beta.1";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var embedded = new ConfigurationModuleSegment
            {
                Kind = ModuleDependencyKind.EmbeddedModule,
                Configuration = new ModuleDependencyConfiguration
                {
                    ModuleName = rootDependency,
                    RequiredVersion = "Auto",
                    VersionSource = ModuleDependencyVersionSource.PSGallery
                }
            };
            var spec = CreateDependencySpec(root.FullName, moduleName, embedded);
            var build = Assert.IsType<ConfigurationBuildSegment>(spec.Segments[0]);
            build.BuildModule!.InstallMissingModulesPrerelease = true;
            var hostedOperations = new FakeHostedOperations();
            var provider = new TransitivePrereleaseMetadataProvider(
                rootDependency,
                childDependency,
                childPrerelease);
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new RecordingPowerShellRunner(_ => new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh")),
                provider,
                hostedOperations);

            var plan = runner.Plan(spec);
            InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            var childManifestReference = Assert.Single(
                plan.EmbeddedModules,
                module => string.Equals(module.ModuleName, childDependency, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("2.0.0", childManifestReference.ModuleVersion);
            var childInstallRequest = Assert.Single(
                hostedOperations.LastDependencies,
                dependency => string.Equals(dependency.Name, childDependency, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(childPrerelease, childInstallRequest.RequiredVersion);
            Assert.Null(childInstallRequest.MinimumVersion);
            var rootLookup = Assert.IsType<ResolvedRequiredModuleReference>(provider.CapturedRequiredModuleReference);
            Assert.Equal("1.0.0", rootLookup.RequiredVersion);
            Assert.Equal("1.0.0-beta.1", rootLookup.ResolvedVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_DuplicateExternalModuleUsesLastDeclarationWithoutResolvingSupersededSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata(
                    "Dependency.Tools",
                    "2.0.0",
                    null,
                    Path.Combine(root.FullName, "Dependency.Tools", "2.0.0")));
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                provider,
                new FakeHostedOperations());
            var superseded = CreateModuleSegment(
                ModuleDependencyKind.ExternalModule,
                "1.0.0",
                ModuleDependencyVersionSource.PSGallery);
            superseded.Configuration!.Guid = "11111111-1111-1111-1111-111111111111";
            var winner = CreateModuleSegment(
                ModuleDependencyKind.ExternalModule,
                "2.0.0",
                ModuleDependencyVersionSource.Installed);
            winner.Configuration!.Guid = "22222222-2222-2222-2222-222222222222";

            var plan = runner.Plan(CreateDependencySpec(
                root.FullName,
                moduleName,
                superseded,
                winner));

            var source = Assert.Single(plan.DependencySourceResolutions);
            Assert.Equal("2.0.0", source.RequiredVersion);
            Assert.Equal("22222222-2222-2222-2222-222222222222", source.Guid);
            Assert.Equal(ModuleDependencyVersionSource.Installed, source.VersionSource);
            Assert.Equal(0, provider.OnlineLookupCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunPreflight_AutoApprovedDonorChecksAllInstalledVersionsBeforeBootstrapping()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string donorName = "Approved.Donor";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0", CsprojPath = null },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = donorName,
                            RequiredVersion = "1.0.0",
                            VersionSource = ModuleDependencyVersionSource.Auto
                        }
                    }
                }
            };
            var hostedOperations = new FakeHostedOperations();
            var provider = new FixedVersionedMetadataProvider(
                new InstalledModuleMetadata(donorName, "1.0.0", null, Path.Combine(root.FullName, "1.0.0")),
                new InstalledModuleMetadata(donorName, "2.0.0", null, Path.Combine(root.FullName, "2.0.0")));
            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider, hostedOperations);

            InvokeEnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(runner, spec);

            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
            Assert.Contains(provider.Requests.SelectMany(static request => request), reference =>
                string.Equals(reference.ModuleName, donorName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reference.RequiredVersion, "1.0.0", StringComparison.OrdinalIgnoreCase));
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
        private readonly InstalledModuleMetadata _latest;
        private readonly string? _onlineVersion;

        internal List<RequiredModuleReference[]> Requests { get; } = new();
        internal int OnlineLookupCalls { get; private set; }

        internal FixedVersionedMetadataProvider(
            InstalledModuleMetadata installed,
            InstalledModuleMetadata? latest = null,
            string? onlineVersion = null)
        {
            _installed = installed;
            _latest = latest ?? installed;
            _onlineVersion = onlineVersion;
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
                [_latest.Name] = _latest
            };

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<RequiredModuleReference>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
        {
            OnlineLookupCalls++;
            return string.IsNullOrWhiteSpace(_onlineVersion)
                ? new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
                : names.ToDictionary(
                    static name => name,
                    _ => ((string?)_onlineVersion, (string?)null),
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class TransitivePrereleaseMetadataProvider : IModuleDependencyMetadataProvider, IModuleDependencyReferenceMetadataProvider
    {
        private readonly string _rootDependency;
        private readonly string _childDependency;
        private readonly string _childPrerelease;

        internal RequiredModuleReference? CapturedRequiredModuleReference { get; private set; }

        internal TransitivePrereleaseMetadataProvider(
            string rootDependency,
            string childDependency,
            string childPrerelease)
        {
            _rootDependency = rootDependency;
            _childDependency = childDependency;
            _childPrerelease = childPrerelease;
        }

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => (names ?? Array.Empty<string>())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToDictionary(
                    static name => name,
                    name => new InstalledModuleMetadata(
                        name,
                        string.Equals(name, _childDependency, StringComparison.OrdinalIgnoreCase) ? "2.0.0" : "1.0.0",
                        null,
                        Path.Combine(Path.GetTempPath(), name)),
                    StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
            => string.Equals(moduleName, _rootDependency, StringComparison.OrdinalIgnoreCase)
                ? new[] { new RequiredModuleReference(_childDependency) }
                : Array.Empty<RequiredModuleReference>();

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(RequiredModuleReference reference)
        {
            if (string.Equals(reference.ModuleName, _rootDependency, StringComparison.OrdinalIgnoreCase))
                CapturedRequiredModuleReference = reference;
            return GetRequiredModulesForInstalledModule(reference.ModuleName);
        }

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => (names ?? Array.Empty<string>())
                .Where(name => string.Equals(name, _rootDependency, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(name, _childDependency, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    static name => name,
                    name => ((string?)(string.Equals(name, _rootDependency, StringComparison.OrdinalIgnoreCase)
                        ? "1.0.0-beta.1"
                        : _childPrerelease), (string?)null),
                    StringComparer.OrdinalIgnoreCase);
    }
}
