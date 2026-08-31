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

    private static PowerShellCompilationProviderTrustPolicy CreateProviderTrust(string providerId)
        => new()
        {
            AllowedPackageIds = new[] { "Generic.Semantic.Provider" },
            AllowedProviderIds = new[] { providerId },
            AllowedPublishers = new[] { "Generic Publisher" },
            AllowedLicenseExpressions = new[] { "MIT" }
        };

    private static (int ExitCode, string StandardOutput, string StandardError) RunProviderProcess(string fileName)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Provider executable did not exit within 60 seconds.");
        return (process.ExitCode, standardOutput, standardError);
    }
}
