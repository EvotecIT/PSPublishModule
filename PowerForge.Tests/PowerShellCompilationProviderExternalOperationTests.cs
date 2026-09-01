using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationProviderPackageTests
{
    [Fact]
    public void StrictNativeAotExecutableExecutesLockedExternalFileOperation()
    {
        if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64)
            return;

        using var providerFixture = ProviderFixture.Create();
        var provider = CreateExternalFileReadProvider();
        providerFixture.Manifest.Providers = new[] { provider };
        var packagePath = providerFixture.PackagePath("filesystem-native-aot.nupkg");
        var resolution = providerFixture.BuildPackage("filesystem-native-aot.nupkg");
        var inputPath = Path.Combine(providerFixture.RootPath, "native-input.txt");
        File.WriteAllText(inputPath, "native-aot-provider-value");
        using var artifactFixture = ScriptFixture.Create(
            "Read-PackageTextCore '" + inputPath.Replace("'", "''", StringComparison.Ordinal) + "'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ExternalFileProviderNativeAot",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            RuntimeIdentifier = "win-x64",
            SelfContained = true,
            SingleFile = true,
            Optimization = PowerShellCompilationExecutableOptimization.NativeAot,
            TimeoutSeconds = 600,
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = CreateProviderTrust(provider.ProviderId)
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(PowerShellCompilationDeploymentModel.NativeAot, result.Manifest!.TargetContract!.Deployment);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.Equal("PE", result.Manifest.DependencyClosure!.NativeExecutable!.Format);
        var run = RunProviderProcess(result.ArtifactPath!);
        Assert.Equal((0, "native-aot-provider-value", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void NativeAotRejectsIncompatibleAdapterResolvedFromProviderPackage()
    {
        using var providerFixture = ProviderFixture.Create();
        var provider = CreateExternalFileReadProvider();
        provider.Adapter.AotCompatible = false;
        providerFixture.Manifest.Providers = new[] { provider };
        var packagePath = providerFixture.PackagePath("filesystem-incompatible.nupkg");
        var resolution = providerFixture.BuildPackage("filesystem-incompatible.nupkg");
        using var artifactFixture = ScriptFixture.Create("Read-PackageTextCore 'input.txt'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "IncompatibleExternalProvider",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            RuntimeIdentifier = "win-x64",
            SelfContained = true,
            SingleFile = true,
            Optimization = PowerShellCompilationExecutableOptimization.NativeAot,
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = CreateProviderTrust(provider.ProviderId)
        });

        Assert.False(result.Succeeded);
        Assert.Contains(provider.ProviderId, result.Error, StringComparison.Ordinal);
        Assert.Contains("AotCompatible", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(artifactFixture.OutputPath));
    }

    [Fact]
    public void StrictExecutableTerminatesHungProcessIsolatedProviderAtDeclaredDeadline()
    {
        using var providerFixture = ProviderFixture.Create();
        var provider = CreateProcessIsolatedProvider();
        providerFixture.Manifest.Providers = new[] { provider };
        var packagePath = providerFixture.PackagePath("process-isolated.nupkg");
        var resolution = providerFixture.BuildPackage("process-isolated.nupkg");
        var markerPath = Path.Combine(providerFixture.RootPath, "isolated-provider.started");
        using var artifactFixture = ScriptFixture.Create(
            "Invoke-PackageIsolatedCore '" + markerPath.Replace("'", "''", StringComparison.Ordinal) + "'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ProcessIsolatedProviderExecutable",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = CreateProviderTrust(provider.ProviderId)
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var run = RunProviderProcess(result.ArtifactPath!);
        stopwatch.Stop();
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("isolation deadline", run.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Isolation deadline took {stopwatch.Elapsed}.");
        Assert.True(File.Exists(markerPath), "The worker did not reach the blocking provider entry point.");
        using (new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        File.Delete(markerPath);
    }

    [Theory]
    [InlineData(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict)]
    [InlineData(PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict)]
    [InlineData(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)]
    public void ProcessIsolatedProviderFailsClosedOutsideStrictExecutableHost(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode)
    {
        using var providerFixture = ProviderFixture.Create();
        var provider = CreateProcessIsolatedProvider();
        providerFixture.Manifest.Providers = new[] { provider };
        var packagePath = providerFixture.PackagePath("process-isolated-host-boundary.nupkg");
        var resolution = providerFixture.BuildPackage("process-isolated-host-boundary.nupkg");
        using var artifactFixture = ScriptFixture.Create("Invoke-PackageIsolatedCore 'input'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ProcessIsolatedProviderHostBoundary",
            kind,
            mode,
            allowUnreviewedDependencyResolution: true)
        {
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = CreateProviderTrust(provider.ProviderId)
        });

        Assert.False(result.Succeeded);
        Assert.Contains("requires a Strict executable host", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(artifactFixture.OutputPath));
    }

    [Fact]
    public void ProcessIsolatedProviderRoundTripsFormerFrameMarkerThroughDedicatedPipe()
    {
        using var providerFixture = ProviderFixture.Create();
        var provider = CreateProcessIsolatedProvider();
        provider.Adapter.EntryPoint!.MethodName = "EchoWithCancellation";
        provider.Adapter.ProcessIsolationTimeoutSeconds = 10;
        providerFixture.Manifest.Providers = new[] { provider };
        var packagePath = providerFixture.PackagePath("process-isolated-framing.nupkg");
        var resolution = providerFixture.BuildPackage("process-isolated-framing.nupkg");
        const string value = "prefix-PowerForge.ProviderWorker/1:payload";
        using var artifactFixture = ScriptFixture.Create("Invoke-PackageIsolatedCore '" + value + "'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ProcessIsolatedProviderFraming",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = CreateProviderTrust(provider.ProviderId)
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = RunProviderProcess(result.ArtifactPath!);
        Assert.Equal((0, value, string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void GeneratedWorkerDispatchIncludesOnlyReachableProvidersAndRequiresPipeCapability()
    {
        using var providerFixture = ProviderFixture.Create();
        var used = CreateProcessIsolatedProvider();
        used.Adapter.EntryPoint!.MethodName = "EchoWithCancellation";
        used.Adapter.ProcessIsolationTimeoutSeconds = 10;
        var unused = CreateProcessIsolatedProvider();
        unused.ProviderId = "generic.external.process-isolation.unused";
        unused.FeatureId = "generic.external.process-isolation.unused";
        unused.CommandName = "Invoke-UnusedIsolatedCore";
        unused.Adapter.Operation = "UnusedProcessIsolation";
        unused.Adapter.EntryPoint!.MethodName = "TouchWithCancellation";
        unused.Adapter.ProcessIsolationTimeoutSeconds = 10;
        providerFixture.Manifest.Providers = new[] { used, unused };
        var packagePath = providerFixture.PackagePath("process-isolated-reachability.nupkg");
        var resolution = providerFixture.BuildPackage("process-isolated-reachability.nupkg");
        using var artifactFixture = ScriptFixture.Create("Invoke-PackageIsolatedCore 'reachable'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ProcessIsolatedProviderReachability",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true,
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = CreateProviderTrust(used.ProviderId, unused.ProviderId)
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShellScript.cs"));
        Assert.Contains(used.ProviderId, generated, StringComparison.Ordinal);
        Assert.DoesNotContain(unused.ProviderId, generated, StringComparison.Ordinal);
        var markerPath = Path.Combine(providerFixture.RootPath, "unreachable-provider.marker");
        var direct = RunProviderProcess(
            result.ArtifactPath!,
            "--powerforge-internal-provider-worker-v2",
            unused.ProviderId,
            markerPath);
        Assert.NotEqual(0, direct.ExitCode);
        Assert.False(File.Exists(markerPath), "Direct command-line invocation reached an unreferenced provider.");
    }

    private static PowerShellCompilationCommandProviderContract CreateProcessIsolatedProvider()
    {
        var provider = Provider(
            "generic.external.process-isolation.deadline",
            "Invoke-PackageIsolatedCore",
            "Success",
            "WaitWithoutCancellation",
            PowerShellCompilationCommandOutput.Projected,
            PowerShellCompilationCommandCardinality.Scalar,
            PowerShellCompilationCommandErrors.Terminating);
        provider.Family = PowerShellCompilationCommandFamily.ExternalOperation;
        provider.Adapter.Operation = "ProcessIsolationDeadline";
        provider.Adapter.AotCompatible = false;
        provider.Adapter.Cancellation = PowerShellCompilationProviderCancellation.ProcessIsolated;
        provider.Adapter.ProcessIsolationTimeoutSeconds = 1;
        provider.Adapter.Cleanup = PowerShellCompilationProviderCleanup.Deterministic;
        return provider;
    }

    private static PowerShellCompilationCommandProviderContract CreateExternalFileReadProvider()
    {
        var provider = Provider(
            "generic.external.filesystem.read-text",
            "Read-PackageTextCore",
            "Success",
            "ReadText",
            PowerShellCompilationCommandOutput.Projected,
            PowerShellCompilationCommandCardinality.Scalar,
            PowerShellCompilationCommandErrors.Terminating);
        provider.Family = PowerShellCompilationCommandFamily.ExternalOperation;
        provider.Adapter.Operation = "ReadText";
        provider.Adapter.Cleanup = PowerShellCompilationProviderCleanup.Deterministic;
        return provider;
    }

    private static PowerShellCompilationProviderTrustPolicy CreateProviderTrust(params string[] providerIds)
        => new()
        {
            AllowedPackageIds = new[] { "Generic.Semantic.Provider" },
            AllowedProviderIds = providerIds,
            AllowedPublishers = new[] { "Generic Publisher" },
            AllowedLicenseExpressions = new[] { "MIT" }
        };

    private static (int ExitCode, string StandardOutput, string StandardError) RunProviderProcess(
        string fileName,
        params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var timedOut = !process.WaitForExit(60_000);
        if (timedOut)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.WaitForExit(10_000);
        }
        if (!Task.WhenAll(standardOutput, standardError).Wait(10_000))
            throw new TimeoutException("Provider executable output did not close within 10 seconds of process exit.");
        if (timedOut)
            throw new TimeoutException("Provider executable did not exit within 60 seconds.");
        return (process.ExitCode, standardOutput.Result, standardError.Result);
    }
}
