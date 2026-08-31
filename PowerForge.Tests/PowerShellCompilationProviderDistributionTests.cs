using PowerForge;
using System.Text.Json.Nodes;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationProviderPackageTests
{
    [Fact]
    public void ReaderLocksRedistributionAndTargetRestrictionsAndEnforcesReviewedPolicy()
    {
        using var fixture = ProviderFixture.Create();
        fixture.Manifest.SupportedRuntimeIdentifiers = new[] { "win-x64", "linux-x64" };
        var resolution = fixture.BuildPackage("restricted.nupkg");
        var reference = new PowerShellCompilationProviderPackageReference(fixture.PackagePath("restricted.nupkg"));

        var package = Assert.Single(resolution.Lock.Packages);
        Assert.True(package.Redistributable);
        Assert.Equal(new[] { "linux-x64", "win-x64" }, package.SupportedRuntimeIdentifiers);
        var targetlessException = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageReader().Resolve(new[] { reference }));
        Assert.Contains("RID-less artifact target", targetlessException.Message, StringComparison.OrdinalIgnoreCase);
        var allowed = new PowerShellCompilationProviderPackageReader().Resolve(
            new[] { reference },
            new PowerShellCompilationProviderTrustPolicy { RequireRedistributable = true },
            runtimeIdentifier: "win-x64");
        Assert.Equal(resolution.Lock.LockSha256, allowed.Lock.LockSha256);

        var targetException = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageReader().Resolve(
                new[] { reference },
                runtimeIdentifier: "osx-arm64"));
        Assert.Contains("does not support runtime identifier", targetException.Message, StringComparison.OrdinalIgnoreCase);

        fixture.BuildPackage("noncanonical-rid.nupkg");
        RewriteJsonEntry<PowerShellCompilationProviderPackageManifest>(
            fixture.PackagePath("noncanonical-rid.nupkg"),
            PowerShellCompilationProviderPackageReader.ManifestPath,
            manifest => manifest.SupportedRuntimeIdentifiers = new[] { "WIN-X64" });
        var canonicalException = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageReader().Resolve(new[]
            {
                new PowerShellCompilationProviderPackageReference(fixture.PackagePath("noncanonical-rid.nupkg"))
            }));
        Assert.Contains("canonical lowercase RID", canonicalException.Message, StringComparison.OrdinalIgnoreCase);

        fixture.Manifest.Redistributable = false;
        fixture.Manifest.SupportedRuntimeIdentifiers = Array.Empty<string>();
        fixture.BuildPackage("non-redistributable.nupkg");
        var redistributionException = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageReader().Resolve(
                new[] { new PowerShellCompilationProviderPackageReference(fixture.PackagePath("non-redistributable.nupkg")) },
                new PowerShellCompilationProviderTrustPolicy { RequireRedistributable = true }));
        Assert.Contains("not approved for redistribution", redistributionException.Message, StringComparison.OrdinalIgnoreCase);

        var nonRedistributableReference = new PowerShellCompilationProviderPackageReference(fixture.PackagePath("non-redistributable.nupkg"));
        var nonRedistributableResolution = new PowerShellCompilationProviderPackageReader().Resolve(new[] { nonRedistributableReference });
        using var artifactFixture = ScriptFixture.Create("Write-PackageNoticeCore 'redistribution'");
        var artifact = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ProviderRedistributionRestriction",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            ProviderPackages = new[] { nonRedistributableReference },
            ExpectedProviderLock = nonRedistributableResolution.Lock
        });
        Assert.False(artifact.Succeeded);
        Assert.Equal(PowerShellCompilationFailureStage.Dependency, artifact.Failure!.Stage);
        Assert.Contains("cannot be delivered", artifact.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRequiresCurrentExplicitDistributionManifestFields()
    {
        using var fixture = ProviderFixture.Create();
        fixture.BuildPackage("legacy-schema.nupkg");
        RewriteJsonEntry<PowerShellCompilationProviderPackageManifest>(
            fixture.PackagePath("legacy-schema.nupkg"),
            PowerShellCompilationProviderPackageReader.ManifestPath,
            manifest => manifest.SchemaVersion = 2);
        var legacyException = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageReader().Resolve(new[]
            {
                new PowerShellCompilationProviderPackageReference(fixture.PackagePath("legacy-schema.nupkg"))
            }));
        Assert.Contains("current provider SDK", legacyException.Message, StringComparison.OrdinalIgnoreCase);

        fixture.BuildPackage("missing-target-review.nupkg");
        RewriteTextEntry(
            fixture.PackagePath("missing-target-review.nupkg"),
            PowerShellCompilationProviderPackageReader.ManifestPath,
            content =>
            {
                var document = JsonNode.Parse(content)!.AsObject();
                document.Remove("SupportedRuntimeIdentifiers");
                return document.ToJsonString();
            });
        var missingException = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageReader().Resolve(new[]
            {
                new PowerShellCompilationProviderPackageReference(fixture.PackagePath("missing-target-review.nupkg"))
            }));
        Assert.Contains("explicitly declare", missingException.Message, StringComparison.OrdinalIgnoreCase);

        fixture.BuildPackage("explicit-portable.nupkg");
        var portable = new PowerShellCompilationProviderPackageReader().Resolve(new[]
        {
            new PowerShellCompilationProviderPackageReference(fixture.PackagePath("explicit-portable.nupkg"))
        });
        Assert.Empty(Assert.Single(portable.Lock.Packages).SupportedRuntimeIdentifiers);
    }

    [Theory]
    [InlineData(PowerShellCompilationArtifactKind.Executable)]
    [InlineData(PowerShellCompilationArtifactKind.Library)]
    [InlineData(PowerShellCompilationArtifactKind.BinaryModule)]
    public void ArtifactBuildRejectsRuntimeRestrictedProviderForRidlessArtifact(
        PowerShellCompilationArtifactKind artifactKind)
    {
        using var providerFixture = ProviderFixture.Create();
        providerFixture.Manifest.SupportedRuntimeIdentifiers = new[] { "linux-x64" };
        var packagePath = providerFixture.PackagePath("runtime-restricted.nupkg");
        var resolution = providerFixture.BuildPackage("runtime-restricted.nupkg");
        using var artifactFixture = ScriptFixture.Create("Write-PackageNoticeCore 'targetless'");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ProviderTargetlessRestriction",
            artifactKind,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            SingleFile = false,
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock
        });

        Assert.False(result.Succeeded);
        Assert.Equal(PowerShellCompilationFailureStage.Dependency, result.Failure!.Stage);
        Assert.Contains("RID-less artifact target", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectInspectionAndLockResolveProviderAgainstEachArtifactRuntimeTarget()
    {
        using var providerFixture = ProviderFixture.Create();
        providerFixture.Manifest.SupportedRuntimeIdentifiers = new[] { "linux-x64" };
        providerFixture.BuildPackage("linux-only-project.nupkg");
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeProviderProjectTargetTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "program.ps1");
            var projectPath = Path.Combine(root, "powerforge.psproject.json");
            var packagePath = Path.Combine(root, "provider.nupkg");
            File.WriteAllText(sourcePath, "[int] $value = 42; return $value");
            File.Copy(providerFixture.PackagePath("linux-only-project.nupkg"), packagePath);
            var target = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Strict,
                "net8.0",
                "win-x64",
                selfContained: false,
                singleFile: false,
                PowerShellCompilationExecutableOptimization.None,
                explicitContract: true);
            var service = new PowerShellCompilationProjectManifestService();
            var manifest = service.Create(projectPath, sourcePath, "TargetAwareProviderProject", target);
            manifest.ProviderPackages = new[] { "provider.nupkg" };
            Assert.Single(manifest.Artifacts).ProviderLock = ".powerforge/locks/providers.lock.json";
            service.Save(projectPath, manifest);
            var workflow = new PowerShellCompilationProjectWorkflowService();

            var results = new[]
            {
                workflow.Analyze(projectPath),
                workflow.Explain(projectPath),
                workflow.Recommend(projectPath),
                workflow.Lock(projectPath)
            };

            Assert.All(results, result =>
            {
                Assert.False(result.Succeeded);
                Assert.Contains("does not support runtime identifier 'win-x64'", Assert.Single(result.Targets).Message, StringComparison.OrdinalIgnoreCase);
            });
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ArtifactBuildRejectsReviewedProviderOutsideItsLockedRuntimeTargets()
    {
        using var providerFixture = ProviderFixture.Create();
        providerFixture.Manifest.SupportedRuntimeIdentifiers = new[] { "linux-x64" };
        var packagePath = providerFixture.PackagePath("linux-only.nupkg");
        var resolution = providerFixture.BuildPackage("linux-only.nupkg");
        using var artifactFixture = ScriptFixture.Create("Write-PackageNoticeCore 'target'");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ProviderTargetRestriction",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            RuntimeIdentifier = "win-x64",
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = new PowerShellCompilationProviderTrustPolicy
            {
                RequireRedistributable = true,
                AllowedPackageIds = new[] { "Generic.Semantic.Provider" },
                AllowedProviderIds = new[] { "generic.command.stream.notice" },
                AllowedPublishers = new[] { "Generic Publisher" },
                AllowedLicenseExpressions = new[] { "MIT" }
            }
        });

        Assert.False(result.Succeeded);
        Assert.Equal(PowerShellCompilationFailureStage.Dependency, result.Failure!.Stage);
        Assert.Contains("does not support runtime identifier 'win-x64'", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
