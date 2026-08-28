using PowerForge;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
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
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.TargetContract!.Explicit);
        Assert.Equal(target.ContractSha256, result.Manifest.TargetContract.ContractSha256);
        Assert.Equal(target.ContractSha256, result.Manifest.Toolchain!.TargetContractSha256);
        Assert.Equal(result.Manifest.DependencyGraph!.LockSha256, result.Manifest.Toolchain.DependencyLockSha256);
        Assert.NotNull(result.Manifest.IrOptimization);
        Assert.Equal(new[] { "constant-folding", "dead-branch-elimination" }, result.Manifest.IrOptimization!.Passes);
        Assert.NotNull(result.Manifest.Boundaries);
        Assert.Equal(1, result.Manifest.Boundaries!.TypedEntryPoints);
        Assert.Equal(0, result.Manifest.Boundaries.RuntimeFallbackUnits);
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
        target.Architecture = "arm64";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationTargetContractService.Normalize(target));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TargetContractNormalizationRejectsCallerAssertedRidPromotion()
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

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationTargetContractService.Normalize(target));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
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
}
