using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed class AzureArtifactsCredentialProviderInstallerTests
{
    [Fact]
    public void InstallForCurrentUser_InstallsNetCoreProviderFromLocalPackage()
    {
        var root = CreateTempRoot();
        try
        {
            var packagePath = Path.Combine(root, "Microsoft.Net8.NuGet.CredentialProvider.zip");
            CreateCredentialProviderPackage(packagePath, "netcore", "CredentialProvider.Microsoft.dll");
            var profile = Path.Combine(root, "profile");

            var installer = CreateInstaller(
                profile,
                new Dictionary<string, string?>
                {
                    ["POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETCORE_PACKAGE"] = packagePath
                });

            var result = installer.InstallForCurrentUser(includeNetFx: false, installNet8: true);

            Assert.True(result.Succeeded);
            Assert.True(result.Changed);
            Assert.Contains(result.Paths, path => path.EndsWith(@"netcore\CredentialProvider.Microsoft\CredentialProvider.Microsoft.dll", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(profile, ".nuget", "plugins", "netcore", "CredentialProvider.Microsoft", "CredentialProvider.Microsoft.dll")));
            Assert.Contains(result.Messages, message => message.Contains("configured local package", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void InstallForCurrentUser_UsesConfiguredInternalMirrorUri()
    {
        var root = CreateTempRoot();
        try
        {
            var packagePath = Path.Combine(root, "mirror-package.zip");
            CreateCredentialProviderPackage(packagePath, "netcore", "CredentialProvider.Microsoft.dll");
            var profile = Path.Combine(root, "profile");
            var mirrorUri = new Uri("https://packages.contoso.example/artifacts/Microsoft.Net8.NuGet.CredentialProvider.zip");

            var installer = CreateInstaller(
                profile,
                new Dictionary<string, string?>
                {
                    ["POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETCORE_PACKAGE"] = mirrorUri.ToString()
                },
                downloadPackage: (uri, destination, _) =>
                {
                    Assert.Equal(mirrorUri, uri);
                    File.Copy(packagePath, destination, overwrite: true);
                    return destination;
                });

            var result = installer.InstallForCurrentUser(includeNetFx: false, installNet8: true);

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(Path.Combine(profile, ".nuget", "plugins", "netcore", "CredentialProvider.Microsoft", "CredentialProvider.Microsoft.dll")));
            Assert.Contains(result.Messages, message => message.Contains("configured URI", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void InstallForCurrentUser_RejectsSha256Mismatch()
    {
        var root = CreateTempRoot();
        try
        {
            var packagePath = Path.Combine(root, "Microsoft.Net8.NuGet.CredentialProvider.zip");
            CreateCredentialProviderPackage(packagePath, "netcore", "CredentialProvider.Microsoft.dll");
            var profile = Path.Combine(root, "profile");

            var installer = CreateInstaller(
                profile,
                new Dictionary<string, string?>
                {
                    ["POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETCORE_PACKAGE"] = packagePath,
                    ["POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETCORE_SHA256"] = new string('0', 64)
                });

            var ex = Assert.Throws<InvalidOperationException>(() => installer.InstallForCurrentUser(includeNetFx: false, installNet8: true));
            Assert.Contains("SHA256 mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void InstallForCurrentUser_InstallsNetFxProviderFromSpecificPackage()
    {
        var root = CreateTempRoot();
        try
        {
            var packagePath = Path.Combine(root, "Microsoft.NetFx48.NuGet.CredentialProvider.zip");
            CreateCredentialProviderPackage(packagePath, "netfx", "CredentialProvider.Microsoft.exe");
            var profile = Path.Combine(root, "profile");

            var installer = CreateInstaller(
                profile,
                new Dictionary<string, string?>
                {
                    ["POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETFX_PACKAGE"] = packagePath,
                    ["POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETFX_SHA256"] = ComputeSha256(packagePath)
                });

            var result = installer.InstallForCurrentUser(includeNetFx: true, installNet8: false);

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(Path.Combine(profile, ".nuget", "plugins", "netfx", "CredentialProvider.Microsoft", "CredentialProvider.Microsoft.exe")));
            Assert.Contains(result.Paths, path => path.EndsWith(@"netfx\CredentialProvider.Microsoft\CredentialProvider.Microsoft.exe", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static AzureArtifactsCredentialProviderInstaller CreateInstaller(
        string profile,
        IReadOnlyDictionary<string, string?> environment,
        Func<Uri, string, TimeSpan, string>? downloadPackage = null)
    {
        return new AzureArtifactsCredentialProviderInstaller(
            new NullLogger(),
            name => environment.TryGetValue(name, out var value) ? value : null,
            () => profile,
            downloadPackage ?? ((_, _, _) => throw new InvalidOperationException("Download was not expected.")));
    }

    private static void CreateCredentialProviderPackage(string packagePath, string runtimeFolder, string providerFileName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"plugins/{runtimeFolder}/CredentialProvider.Microsoft/{providerFileName}");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("test credential provider");
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
