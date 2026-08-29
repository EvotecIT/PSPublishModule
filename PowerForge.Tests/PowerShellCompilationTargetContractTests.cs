using PowerForge;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void ResolvedPackageCatalogRejectsContentThatDiffersFromReviewedCompilerPackage()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "powerforge-package-provenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "obj"));
        try
        {
            File.WriteAllText(
                Path.Combine(workspace, "obj", "project.assets.json"),
                "{\"libraries\":{\"Compiler.Package/1.2.3\":{\"type\":\"package\",\"sha512\":\"" +
                Convert.ToBase64String(Enumerable.Repeat((byte)1, 64).ToArray()) + "\"}}}");
            var graph = new PowerShellCompilationDependencyGraph
            {
                Nodes = new[]
                {
                    new PowerShellCompilationDependencyNode
                    {
                        Kind = PowerShellCompilationDependencyNodeKind.NuGetPackage,
                        Roles = PowerShellCompilationDependencyGraphRole.Build,
                        Identity = new PowerShellCompilationDependencyIdentity
                        {
                            Name = "Compiler.Package",
                            Version = "1.2.3",
                            ContentHashAlgorithm = "SHA-512",
                            ContentHash = Convert.ToBase64String(Enumerable.Repeat((byte)2, 64).ToArray())
                        }
                    }
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                PowerShellCompilationResolvedPackageCatalog.ReadAndVerify(workspace, graph));

            Assert.Contains("content hash", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TargetContractSeparatesExternalRuntimeRequirementFromFallbackCapability()
    {
        var hybridLibrary = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Hybrid,
            "net10.0",
            runtimeIdentifier: null,
            selfContained: false,
            singleFile: false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var hybridExecutable = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Hybrid,
            "net10.0",
            "win-x64",
            selfContained: false,
            singleFile: true,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var selfContainedExecutable = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Hybrid,
            "net10.0",
            "win-x64",
            selfContained: true,
            singleFile: true,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var binaryModule = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            "net10.0",
            runtimeIdentifier: null,
            selfContained: false,
            singleFile: false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);

        Assert.Equal(PowerShellCompilationRuntimeRequirement.DotNet, hybridLibrary.RuntimeRequirement);
        Assert.False(hybridLibrary.AllowsPowerShellRuntimeEvaluation);
        Assert.Equal(PowerShellCompilationRuntimeRequirement.DotNet, hybridExecutable.RuntimeRequirement);
        Assert.True(hybridExecutable.AllowsPowerShellRuntimeEvaluation);
        Assert.Equal(PowerShellCompilationRuntimeRequirement.None, selfContainedExecutable.RuntimeRequirement);
        Assert.True(selfContainedExecutable.AllowsPowerShellRuntimeEvaluation);
        Assert.Equal(PowerShellCompilationRuntimeRequirement.PowerShell, binaryModule.RuntimeRequirement);
        Assert.True(binaryModule.AllowsPowerShellRuntimeEvaluation);
    }

    [Fact]
    public void Build_StrictLibraryConsumesExplicitTargetAndEmitsIntegrityBoundEvidence()
    {
        using var fixture = ArtifactFixture.Create("function Get-TargetValue { return 42 }", ".psm1");
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            "net10.0",
            runtimeIdentifier: null,
            selfContained: false,
            singleFile: false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ExplicitTarget",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetContract = target,
            EmitSource = true,
            BoundaryRuntimeProfile = new PowerShellCompilationBoundaryRuntimeProfile
            {
                Workload = "target-contract-profile",
                RuntimeIdentifier = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                BaselineDurationNanoseconds = 100,
                BoundaryDurationNanoseconds = 200,
                BoundaryInvocations = 2,
                EstimatedOverheadNanosecondsPerBoundary = 50,
                EstimatedOverheadRatio = 0.5,
                Advisory = "profile advisory"
            }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.TargetContract!.Explicit);
        Assert.Equal(target.ContractSha256, result.Manifest.TargetContract.ContractSha256);
        Assert.Equal(target.ContractSha256, result.Manifest.Toolchain!.TargetContractSha256);
        Assert.Equal(result.Manifest.DependencyGraph!.LockSha256, result.Manifest.Toolchain.DependencyLockSha256);
        Assert.NotNull(result.Manifest.IrOptimization);
        Assert.Equal(
            new[]
            {
                "constant-folding",
                "dead-branch-elimination",
                "identity-conversion-elimination"
            },
            result.Manifest.IrOptimization!.Passes);
        Assert.Equal(
            new[]
            {
                "allocation-reduction",
                "pipeline-stage-fusion",
                "command-region-coalescing",
                "specialized-collection-loops",
                "cached-conversion-plans"
            },
            result.Manifest.IrOptimization.BackendOptimizations);
        Assert.Equal(new[] { "authored-source-sequence-mapping" }, result.Manifest.IrOptimization.Instrumentation);
        Assert.NotNull(result.Manifest.Boundaries);
        Assert.Equal(1, result.Manifest.Boundaries!.TypedEntryPoints);
        Assert.Equal(0, result.Manifest.Boundaries.RuntimeFallbackUnits);
        Assert.Equal("target-contract-profile", result.Manifest.Boundaries.RuntimeProfile!.Workload);
        Assert.Equal("profile advisory", result.Manifest.Boundaries.Advisory);
        Assert.Contains(result.Manifest.Files, static file => file.Role == "TargetContract");
        Assert.Contains(result.Manifest.Files, static file => file.Role == "BuildProvenance");
        Assert.Contains(result.Manifest.Files, static file => file.Role == "Sbom");
        Assert.True(File.Exists(Path.Combine(result.GeneratedSourcePath!, "PowerForge.TargetContract.json")));
        using (var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "global.json"))))
        {
            var sdk = globalJson.RootElement.GetProperty("sdk");
            Assert.Equal(result.Manifest.Toolchain.DotNetSdkVersion, sdk.GetProperty("version").GetString());
            Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
        }
        Assert.All(result.Manifest.Files, static file => Assert.False(string.IsNullOrWhiteSpace(file.Sha256)));
    }

    [Fact]
    public void TargetContractNormalizationRejectsHashDrift()
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            "net10.0",
            "win-x64",
            selfContained: true,
            singleFile: true,
            PowerShellCompilationExecutableOptimization.NativeAot,
            explicitContract: true);
        target.SingleFile = false;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationTargetContractService.Normalize(target));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TargetContractNormalizationPromotesImplicitRequestWithoutChangingCanonicalIdentity()
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            "net10.0",
            runtimeIdentifier: null,
            selfContained: false,
            singleFile: false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: false);
        var originalHash = target.ContractSha256;

        var normalized = PowerShellCompilationTargetContractService.Normalize(target);

        Assert.True(normalized.Explicit);
        Assert.Equal(originalHash, normalized.ContractSha256);
    }

    [Fact]
    public void TargetContractNormalizationAcceptsAndMigratesLegacyV1Identity()
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            "net10.0",
            runtimeIdentifier: null,
            selfContained: false,
            singleFile: false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: false);
        target.SchemaVersion = 1;
        target.ContractSha256 = PowerShellCompilationTargetContractService.ComputeSha256(target);
        var legacyHash = target.ContractSha256;

        var normalized = PowerShellCompilationTargetContractService.Normalize(target);

        Assert.Equal(2, normalized.SchemaVersion);
        Assert.True(normalized.Explicit);
        Assert.NotEqual(legacyHash, normalized.ContractSha256);
        Assert.Equal(PowerShellCompilationTargetContractService.ComputeSha256(normalized), normalized.ContractSha256);
    }

    [Fact]
    public void TargetContractNormalizationRejectsRidDerivedFieldConflictEvenWithMatchingHash()
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            "net10.0",
            "win-x64",
            selfContained: true,
            singleFile: true,
            PowerShellCompilationExecutableOptimization.NativeAot,
            explicitContract: true);
        target.OperatingSystem = "linux";
        target.ContractSha256 = PowerShellCompilationTargetContractService.ComputeSha256(target);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationTargetContractService.Normalize(target));

        Assert.Contains("runtime identifier", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TargetContractNormalizationRecomputesCallerAssertedRidPromotion()
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            "net10.0",
            "linux-arm64",
            selfContained: true,
            singleFile: true,
            PowerShellCompilationExecutableOptimization.NativeAot,
            explicitContract: true);
        target.SupportLevel = "Supported";
        target.ContractSha256 = PowerShellCompilationTargetContractService.ComputeSha256(target);

        var normalized = PowerShellCompilationTargetContractService.Normalize(target);

        Assert.Equal("Experimental", normalized.SupportLevel);
        Assert.Equal(normalized.ContractSha256, PowerShellCompilationTargetContractService.ComputeSha256(normalized));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TargetContractNormalizationMigratesPreviouslyExperimentalCertifiedTuple(int schemaVersion)
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            "net10.0",
            "win-x64",
            selfContained: false,
            singleFile: true,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: false);
        target.SchemaVersion = schemaVersion;
        target.SupportLevel = "Experimental";
        target.ContractSha256 = PowerShellCompilationTargetContractService.ComputeSha256(target);

        var normalized = PowerShellCompilationTargetContractService.Normalize(target);

        Assert.Equal(2, normalized.SchemaVersion);
        Assert.Equal("Supported", normalized.SupportLevel);
        Assert.True(normalized.Explicit);
        Assert.Equal(normalized.ContractSha256, PowerShellCompilationTargetContractService.ComputeSha256(normalized));
    }

    [Theory]
    [InlineData("win-x64", PowerShellCompilationExecutableOptimization.None, false, "Supported")]
    [InlineData("linux-x64", PowerShellCompilationExecutableOptimization.NativeAot, true, "Supported")]
    [InlineData("osx-arm64", PowerShellCompilationExecutableOptimization.NativeAot, true, "Experimental")]
    [InlineData("linux-arm64", PowerShellCompilationExecutableOptimization.NativeAot, true, "Experimental")]
    [InlineData("win-x64", PowerShellCompilationExecutableOptimization.Trimmed, true, "Experimental")]
    public void TargetContractPromotionIsLimitedToTargetHostCertifiedProfiles(
        string runtimeIdentifier,
        PowerShellCompilationExecutableOptimization optimization,
        bool selfContained,
        string expected)
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            "net10.0",
            runtimeIdentifier,
            selfContained,
            singleFile: true,
            optimization,
            explicitContract: true);

        Assert.Equal(expected, target.SupportLevel);
        Assert.Equal(target.ContractSha256, PowerShellCompilationTargetContractService.ComputeSha256(target));
    }

    [Fact]
    public void TargetContractDoesNotPromoteAnUntestedFrameworkOnACertifiedRid()
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            "net8.0",
            "win-x64",
            selfContained: true,
            singleFile: true,
            PowerShellCompilationExecutableOptimization.NativeAot,
            explicitContract: true);

        Assert.Equal("Experimental", target.SupportLevel);
    }

    [Fact]
    public void ReadyToRunTargetRemainsBenchmarkOnly()
    {
        using var fixture = ArtifactFixture.Create("return 1");
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            "net10.0",
            "win-x64",
            selfContained: true,
            singleFile: true,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        target.Deployment = PowerShellCompilationDeploymentModel.ReadyToRun;
        target.ContractSha256 = PowerShellCompilationTargetContractService.ComputeSha256(target);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ReadyToRunPublicRejection",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetContract = target
        };

        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationArtifactBuilder().Build(spec));

        Assert.Contains("benchmark-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCacheRestoresOnlyACompleteContentAddressedSameTargetEntry()
    {
        using var fixture = ArtifactFixture.Create("function Get-CachedValue { return 17 }", ".psm1");
        var cache = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Cache", Guid.NewGuid().ToString("N"));
        PowerShellCompilationBuildResult Build(string framework) => new PowerShellCompilationArtifactBuilder().Build(
            new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                "PowerForge.CachedTarget",
                PowerShellCompilationArtifactKind.Library,
                PowerShellCompilationMode.Strict,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = framework,
                BuildCacheDirectory = cache,
                UseBuildCache = true
            });
        try
        {
            var first = Build("net10.0");
            var second = Build("net10.0");
            var otherTarget = Build("net8.0");

            Assert.True(first.Succeeded, first.Error + Environment.NewLine + first.BuildOutput);
            Assert.False(first.Manifest!.BuildCache!.Hit);
            Assert.Equal("StoredAfterMiss", first.Manifest.BuildCache.Reason);
            Assert.True(second.Succeeded, second.Error + Environment.NewLine + second.BuildOutput);
            Assert.True(second.Manifest!.BuildCache!.Hit);
            Assert.Equal(first.Manifest.BuildCache.Key, second.Manifest.BuildCache.Key);
            Assert.Contains("content-addressed hit", second.BuildOutput, StringComparison.OrdinalIgnoreCase);
            Assert.True(otherTarget.Succeeded, otherTarget.Error + Environment.NewLine + otherTarget.BuildOutput);
            Assert.False(otherTarget.Manifest!.BuildCache!.Hit);
            Assert.NotEqual(first.Manifest.BuildCache.Key, otherTarget.Manifest.BuildCache.Key);
        }
        finally
        {
            try { Directory.Delete(cache, recursive: true); } catch { }
        }
    }

    [Fact]
    public void BuildCacheKeyIncludesProducingHostIdentity()
    {
        using var fixture = ArtifactFixture.Create("function Get-CachedValue { return 17 }", ".psm1");
        var workspace = Path.Combine(fixture.RootPath, "cache-key-workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "input.txt"), "stable");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HostBoundCache",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            UseBuildCache = true
        };
        var target = PowerShellCompilationTargetContractService.Create(
            spec.Kind, spec.Mode, "net10.0", null, false, false,
            PowerShellCompilationExecutableOptimization.None, explicitContract: false);
        var graph = new PowerShellCompilationDependencyGraph { LockSha256 = "graph-lock" };
        PowerShellCompilationToolchainEvidence Toolchain(string operatingSystem) => new()
        {
            DotNetSdkVersion = "10.0.100",
            CompilerVersion = "1.0.0",
            CompilerSha256 = "compiler-hash",
            DotNetSdkSha256 = "sdk-hash",
            BuildOperatingSystem = operatingSystem,
            BuildArchitecture = "X64"
        };

        var windows = PowerShellCompilationArtifactBuildCache.CreateEvidence(spec, workspace, target, graph, Toolchain("Windows"));
        var linux = PowerShellCompilationArtifactBuildCache.CreateEvidence(spec, workspace, target, graph, Toolchain("Linux"));

        Assert.NotEqual(windows.Key, linux.Key);
    }

    [Fact]
    public void BuildCacheRestoreFingerprintIncludesActualResolvedPackageBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.CacheInputs", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var packageRoot = Path.Combine(root, "packages");
        var package = Path.Combine(packageRoot, "example.package", "1.0.0");
        Directory.CreateDirectory(Path.Combine(workspace, "obj"));
        Directory.CreateDirectory(package);
        var payload = Path.Combine(package, "lib.dll");
        File.WriteAllText(payload, "first");
        File.WriteAllText(
            Path.Combine(workspace, "obj", "project.assets.json"),
            JsonSerializer.Serialize(new
            {
                packageFolders = new Dictionary<string, object> { [packageRoot + Path.DirectorySeparatorChar] = new { } },
                libraries = new Dictionary<string, object>
                {
                    ["example.package/1.0.0"] = new { type = "package", path = "example.package/1.0.0" }
                }
            }));
        try
        {
            var first = PowerShellCompilationArtifactBuildCache.ComputeResolvedRestoreInputsSha256(workspace);
            File.WriteAllText(payload, "second");
            var second = PowerShellCompilationArtifactBuildCache.ComputeResolvedRestoreInputsSha256(workspace);

            Assert.NotEqual(first, second);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_StrictBoundaryEvidenceCountsEachHostedRegionSite()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-TwoRegions { [CmdletBinding()] param([int] $Value) Get-RegionText; $Value += 1; Get-RegionText; return $Value }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.BoundarySites",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.Boundaries!.TypedEntryPoints);
        Assert.Equal(2, result.Manifest.Boundaries.HostedRegionSites);
        Assert.Equal(3, result.Manifest.Boundaries.StaticBoundarySites);
    }

    [Fact]
    public void BuildCacheRejectsPayloadWithReparsePointAncestor()
    {
        using var fixture = ArtifactFixture.Create("function Get-CachedValue { return 17 }", ".psm1");
        var cache = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Cache", Guid.NewGuid().ToString("N"));
        var outside = cache + ".outside";
        PowerShellCompilationBuildResult Build() => new PowerShellCompilationArtifactBuilder().Build(
            new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                "PowerForge.LinkedCacheTarget",
                PowerShellCompilationArtifactKind.Library,
                PowerShellCompilationMode.Strict,
                allowUnreviewedDependencyResolution: true)
            {
                BuildCacheDirectory = cache,
                UseBuildCache = true
            });

        string? payload = null;
        try
        {
            var first = Build();
            Assert.True(first.Succeeded, first.Error + Environment.NewLine + first.BuildOutput);
            var key = first.Manifest!.BuildCache!.Key;
            var entry = Path.Combine(cache, key.Substring(0, 2), key);
            payload = Path.Combine(entry, "payload");
            Directory.Move(payload, outside);
            Directory.CreateSymbolicLink(payload, outside);

            var guarded = Build();

            Assert.True(guarded.Succeeded, guarded.Error + Environment.NewLine + guarded.BuildOutput);
            Assert.False(guarded.Manifest!.BuildCache!.Hit);
            Assert.Equal("ExistingEntryUnavailable", guarded.Manifest.BuildCache.Reason);
        }
        finally
        {
            if (payload is not null && Directory.Exists(payload) && (File.GetAttributes(payload) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(payload);
            try { Directory.Delete(cache, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void BuildCacheTreatsSemanticallyMalformedManifestAsAMiss()
    {
        using var fixture = ArtifactFixture.Create("function Get-CachedValue { return 17 }", ".psm1");
        var cache = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Cache", Guid.NewGuid().ToString("N"));
        PowerShellCompilationBuildResult Build() => new PowerShellCompilationArtifactBuilder().Build(
            new PowerShellCompilationBuildSpec(
                fixture.ScriptPath, fixture.OutputPath, "PowerForge.MalformedCache",
                PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
                allowUnreviewedDependencyResolution: true)
            {
                BuildCacheDirectory = cache,
                UseBuildCache = true
            });
        try
        {
            var first = Build();
            Assert.True(first.Succeeded, first.Error + Environment.NewLine + first.BuildOutput);
            var key = first.Manifest!.BuildCache!.Key;
            File.WriteAllText(Path.Combine(cache, key.Substring(0, 2), key, "cache-manifest.json"),
                JsonSerializer.Serialize(new { schemaVersion = 1, key, files = (object?)null }));

            var rebuilt = Build();

            Assert.True(rebuilt.Succeeded, rebuilt.Error + Environment.NewLine + rebuilt.BuildOutput);
            Assert.False(rebuilt.Manifest!.BuildCache!.Hit);
            Assert.DoesNotContain("content-addressed hit", rebuilt.BuildOutput, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(Path.Combine(cache, key.Substring(0, 2), key, "cache-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    key,
                    files = new[] { new { path = "bad\0path", sha256 = "00", sizeBytes = 0 } }
                }));

            var invalidPath = Build();

            Assert.True(invalidPath.Succeeded, invalidPath.Error + Environment.NewLine + invalidPath.BuildOutput);
            Assert.False(invalidPath.Manifest!.BuildCache!.Hit);
            Assert.DoesNotContain("content-addressed hit", invalidPath.BuildOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(cache, recursive: true); } catch { }
        }
    }

    [Fact]
    public void BuildCacheRejectsConfiguredReparsePointRoot()
    {
        using var fixture = ArtifactFixture.Create("function Get-CachedValue { return 17 }", ".psm1");
        var cache = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Cache", Guid.NewGuid().ToString("N"));
        var outside = cache + ".outside";
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(cache, outside);
        try
        {
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath, fixture.OutputPath, "PowerForge.LinkedCacheRoot",
                PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
                allowUnreviewedDependencyResolution: true)
            {
                BuildCacheDirectory = cache,
                UseBuildCache = true
            });

            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            Assert.False(result.Manifest!.BuildCache!.Hit);
            Assert.Equal("UnsafeCacheRoot", result.Manifest.BuildCache.Reason);
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            if (Directory.Exists(cache) && (File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(cache);
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void BuildCacheTreatsLockedManifestAsSafeMiss()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Cache", Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "cache");
        var publish = Path.Combine(root, "publish");
        var output = Path.Combine(root, "output");
        var source = Path.Combine(root, "Source.ps1");
        var key = new string('a', 64);
        var entry = Path.Combine(cache, key.Substring(0, 2), key);
        Directory.CreateDirectory(entry);
        Directory.CreateDirectory(publish);
        File.WriteAllText(source, "function Get-Proof { return 1 }");
        File.WriteAllText(Path.Combine(entry, ".complete"), key);
        var manifestPath = Path.Combine(entry, "cache-manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            key,
            files = new[] { new { path = "Proof.dll", sha256 = new string('b', 64), sizeBytes = 1 } }
        }));
        var spec = new PowerShellCompilationBuildSpec(
            source,
            output,
            "LockedCache",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            BuildCacheDirectory = cache,
            UseBuildCache = true
        };
        var evidence = new PowerShellCompilationBuildCacheEvidence { Key = key };
        try
        {
            using var locked = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var restored = PowerShellCompilationArtifactBuildCache.TryRestore(spec, evidence, publish);

            Assert.False(restored);
            Assert.False(evidence.Hit);
            Assert.Equal("EntryUnavailable", evidence.Reason);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GeneratedWorkspaceStartsInsideCompilerOwnedBuildIsolationBoundary()
    {
        var workspace = PowerShellCompilationWorkspace.Create(keep: true);
        var path = workspace.Path;
        try
        {
            Assert.True(File.Exists(Path.Combine(path, "Directory.Build.props")));
            Assert.True(File.Exists(Path.Combine(path, "Directory.Build.targets")));
            Assert.True(File.Exists(Path.Combine(path, "Directory.Packages.props")));
            var nugetConfig = File.ReadAllText(Path.Combine(path, "NuGet.Config"));
            Assert.Contains("<clear", nugetConfig, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("https://api.nuget.org/v3/index.json", nugetConfig, StringComparison.Ordinal);
        }
        finally
        {
            workspace.Dispose();
            try { Directory.Delete(path, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CompiledMethodRetainsThePublishedCompleteConstructorSignature()
    {
        var signature = new[]
        {
            typeof(string), typeof(string), typeof(string), typeof(PowerShellCompilationParameter[]), typeof(int),
            typeof(string), typeof(bool), typeof(bool), typeof(string[]), typeof(bool), typeof(bool),
            typeof(PowerShellCompilationCommandBinding), typeof(bool), typeof(string), typeof(int), typeof(int),
            typeof(int), typeof(PowerShellCompilationSourceMapEntry[]), typeof(PowerShellCompilationCommandProviderContract[]),
            typeof(string), typeof(string[]), typeof(string), typeof(string)
        };

        Assert.NotNull(typeof(PowerShellCompiledMethod).GetConstructor(signature));
    }
}
