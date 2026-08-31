using System.IO.Compression;
using Microsoft.Management.Infrastructure;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationProviderPackageTests
{
    [WindowsFact]
    public void ProviderNativeAssetIsLockedSelectedByExactRidAndValidatedBeforeUse()
    {
        using var fixture = ProviderFixture.Create();
        fixture.Manifest.SupportedRuntimeIdentifiers = new[] { "win-x64" };
        var packagePath = fixture.PackagePath("native-provider.nupkg");
        var nativePath = GetManagementNativeBridgePath();
        var resolution = BuildWithNativeAsset(fixture, packagePath, nativePath);

        var package = Assert.Single(resolution.Lock.Packages);
        var locked = Assert.Single(package.NativeAssets);
        Assert.Equal("win-x64", locked.RuntimeIdentifier);
        Assert.Equal("microsoft.management.infrastructure.native.unmanaged.dll", locked.FileName);
        Assert.Equal("PE", locked.Format);
        Assert.Equal("x64", locked.Architecture);
        Assert.NotEmpty(locked.ImportedLibraries);
        Assert.Equal(locked.Sha256, Assert.Single(resolution.RuntimeNativeAssets).Asset.Sha256);
        Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationProviderPackageReader().Resolve(
            new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            runtimeIdentifier: "linux-x64"));

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry(locked.Path)!;
            byte[] bytes;
            using (var source = entry.Open())
            using (var memory = new MemoryStream())
            {
                source.CopyTo(memory);
                bytes = memory.ToArray();
            }
            entry.Delete();
            bytes[0] ^= 0xff;
            var replacement = archive.CreateEntry(locked.Path, CompressionLevel.Optimal);
            replacement.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var destination = replacement.Open();
            destination.Write(bytes);
        }

        var tampered = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationProviderPackageReader().Resolve(
            new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            runtimeIdentifier: "win-x64"));
        Assert.Contains("SHA-256", tampered.Message, StringComparison.OrdinalIgnoreCase);

        var wrongArchitecture = Assert.Throws<InvalidDataException>(() =>
            PowerShellCompilationProviderPackageReader.InspectNativeAsset(
                nativePath,
                "runtimes/win-arm64/native/bridge.dll",
                "win-arm64"));
        Assert.Contains("architecture", wrongArchitecture.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderNativeClosureRejectsArbitrarySuffixAndUndeclaredTransitiveImport()
    {
        Assert.True(PowerShellNativeLibraryName.CanResolve("win-x64", "bridge", "bridge.dll"));
        Assert.False(PowerShellNativeLibraryName.CanResolve("win-x64", "bridge.dll", "bridge"));
        Assert.False(PowerShellNativeLibraryName.CanResolve("linux-x64", "libbridge.so", "bridge"));
        Assert.False(PowerShellNativeLibraryName.CanResolve("win-x64", "bridge.dll", "bridge.dll.backup"));

        var asset = new PowerShellCompilationProviderNativeAsset
        {
            Path = "runtimes/win-x64/native/bridge.dll",
            FileName = "bridge.dll",
            RuntimeIdentifier = "win-x64",
            ImportedLibraries = new[] { "undeclared-provider-dependency.dll" }
        };
        var missing = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationProviderPackageReader.EnsureNativeImportClosure(new[] { asset }, "provider.nupkg"));
        Assert.Contains("undeclared-provider-dependency.dll", missing.Message, StringComparison.Ordinal);

        PowerShellCompilationProviderPackageReader.EnsureNativeImportClosure(
            new[]
            {
                asset,
                new PowerShellCompilationProviderNativeAsset
                {
                    Path = "runtimes/win-x64/native/undeclared-provider-dependency.dll",
                    FileName = "undeclared-provider-dependency.dll",
                    RuntimeIdentifier = "win-x64"
                }
            },
            "provider.nupkg");
    }

    [Fact]
    public void ProviderSdkRejectsManagedAssemblyOrDuplicatePathDeclaredAsNativeAsset()
    {
        using var fixture = ProviderFixture.Create();
        fixture.Manifest.SupportedRuntimeIdentifiers = new[] { "win-x64" };
        var providerAssembly = typeof(Generic.Semantic.Provider.NoticeAdapter).Assembly.Location;
        var request = new PowerShellCompilationProviderPackageBuildRequest(
            fixture.PackagePath("managed-native.nupkg"),
            fixture.Manifest)
        {
            Assemblies = new[]
            {
                new PowerShellCompilationProviderAssemblyInput(providerAssembly, "lib/net8.0/Generic.Semantic.Provider.dll")
            },
            NativeAssets = new[]
            {
                new PowerShellCompilationProviderNativeAssetInput(providerAssembly, "runtimes/win-x64/native/managed.dll", "win-x64")
            }
        };
        var managed = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageBuilder().Build(request));
        Assert.Contains("managed metadata", managed.Message, StringComparison.OrdinalIgnoreCase);

        request.NativeAssets = new[]
        {
            new PowerShellCompilationProviderNativeAssetInput(providerAssembly, "runtimes/win-x64/native/duplicate.bin", "win-x64"),
            new PowerShellCompilationProviderNativeAssetInput(providerAssembly, "runtimes/win-x64/native/duplicate.bin", "win-x64")
        };
        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageBuilder().Build(request));
        Assert.Contains("selected more than once", duplicate.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PowerShellCompilationProviderResolution BuildWithNativeAsset(
        ProviderFixture fixture,
        string packagePath,
        string nativePath)
        => new PowerShellCompilationProviderPackageBuilder().Build(
            new PowerShellCompilationProviderPackageBuildRequest(packagePath, fixture.Manifest)
            {
                Assemblies = new[]
                {
                    new PowerShellCompilationProviderAssemblyInput(
                        typeof(Generic.Semantic.Provider.NoticeAdapter).Assembly.Location,
                        "lib/net8.0/Generic.Semantic.Provider.dll")
                },
                NativeAssets = new[]
                {
                    new PowerShellCompilationProviderNativeAssetInput(
                        nativePath,
                        "runtimes/win-x64/native/microsoft.management.infrastructure.native.unmanaged.dll",
                        "win-x64")
                }
            });

    private static string GetManagementNativeBridgePath()
    {
        var managedDirectory = Path.GetDirectoryName(typeof(CimSession).Assembly.Location)!;
        var nativePath = Path.GetFullPath(Path.Combine(
            managedDirectory,
            "..", "..", "native",
            "microsoft.management.infrastructure.native.unmanaged.dll"));
        Assert.True(File.Exists(nativePath), nativePath);
        return nativePath;
    }
}
