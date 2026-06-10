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
            Assert.Contains(result.Paths, path => path.EndsWith(CredentialProviderRelativePath("netcore", "CredentialProvider.Microsoft.dll"), StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(CredentialProviderPath(profile, "netcore", "CredentialProvider.Microsoft.dll")));
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
            Assert.True(File.Exists(CredentialProviderPath(profile, "netcore", "CredentialProvider.Microsoft.dll")));
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
            Assert.True(File.Exists(CredentialProviderPath(profile, "netfx", "CredentialProvider.Microsoft.exe")));
            Assert.Contains(result.Paths, path => path.EndsWith(CredentialProviderRelativePath("netfx", "CredentialProvider.Microsoft.exe"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void InstallForCurrentUser_UsesInstalledArtefactsModulePackage()
    {
        var root = CreateTempRoot();
        try
        {
            var profile = Path.Combine(root, "profile");
            var modulePath = Path.Combine(root, "Modules");
            var packagePath = CreateArtefactsModulePackage(modulePath, "netcore", "CredentialProvider.Microsoft.dll");

            var installer = CreateInstaller(
                profile,
                new Dictionary<string, string?>
                {
                    ["PSModulePath"] = modulePath
                });

            var result = installer.InstallForCurrentUser(includeNetFx: false, installNet8: true);

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(CredentialProviderPath(profile, "netcore", "CredentialProvider.Microsoft.dll")));
            Assert.Contains(result.Messages, message => message.Contains("PSPublishModule.Artefacts", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Messages, message => message.Contains(packagePath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void InstallForCurrentUser_AutoInstallsArtefactsModuleBeforePublicFallback()
    {
        var root = CreateTempRoot();
        try
        {
            var profile = Path.Combine(root, "profile");
            var modulePath = Path.Combine(root, "Modules");
            Directory.CreateDirectory(modulePath);
            var runner = new StubPowerShellRunner(request =>
            {
                Assert.Contains("Install-PSResource", request.CommandText, StringComparison.Ordinal);
                Assert.Contains("Falling back to Install-Module", request.CommandText, StringComparison.Ordinal);
                CreateArtefactsModulePackage(modulePath, "netcore", "CredentialProvider.Microsoft.dll");
                return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe");
            });

            var installer = CreateInstaller(
                profile,
                new Dictionary<string, string?>
                {
                    ["PSModulePath"] = modulePath
                },
                runner: runner);

            var result = installer.InstallForCurrentUser(includeNetFx: false, installNet8: true);

            Assert.True(result.Succeeded);
            Assert.Equal(1, runner.CallCount);
            Assert.True(File.Exists(CredentialProviderPath(profile, "netcore", "CredentialProvider.Microsoft.dll")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void InstallForCurrentUser_UsesNewestArtefactsModuleVersion()
    {
        var root = CreateTempRoot();
        try
        {
            var profile = Path.Combine(root, "profile");
            var modulePath = Path.Combine(root, "Modules");
            var olderPackagePath = CreateArtefactsModulePackage(modulePath, "netcore", "CredentialProvider.Microsoft.dll", moduleVersion: "1.0.9");
            var newerPackagePath = CreateArtefactsModulePackage(modulePath, "netcore", "CredentialProvider.Microsoft.dll", moduleVersion: "1.0.10");

            var installer = CreateInstaller(
                profile,
                new Dictionary<string, string?>
                {
                    ["PSModulePath"] = modulePath
                });

            var result = installer.InstallForCurrentUser(includeNetFx: false, installNet8: true);

            Assert.True(result.Succeeded);
            Assert.Contains(result.Messages, message => message.Contains(newerPackagePath, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Messages, message => message.Contains(olderPackagePath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void InstallForCurrentUser_RepairsIncompleteProviderDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            var packagePath = Path.Combine(root, "Microsoft.Net8.NuGet.CredentialProvider.zip");
            CreateCredentialProviderPackage(packagePath, "netcore", "CredentialProvider.Microsoft.dll");
            var profile = Path.Combine(root, "profile");
            var incompleteDirectory = Path.Combine(profile, ".nuget", "plugins", "netcore", "CredentialProvider.Microsoft");
            Directory.CreateDirectory(incompleteDirectory);
            File.WriteAllText(Path.Combine(incompleteDirectory, "leftover.txt"), "old");

            var installer = CreateInstaller(
                profile,
                new Dictionary<string, string?>
                {
                    ["POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETCORE_PACKAGE"] = packagePath
                });

            var result = installer.InstallForCurrentUser(includeNetFx: false, installNet8: true);

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(CredentialProviderPath(profile, "netcore", "CredentialProvider.Microsoft.dll")));
            Assert.False(File.Exists(Path.Combine(incompleteDirectory, "leftover.txt")));
            Assert.Contains(result.Messages, message => message.Contains("incomplete", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static AzureArtifactsCredentialProviderInstaller CreateInstaller(
        string profile,
        IReadOnlyDictionary<string, string?> environment,
        Func<Uri, string, TimeSpan, string>? downloadPackage = null,
        IPowerShellRunner? runner = null)
    {
        return new AzureArtifactsCredentialProviderInstaller(
            runner ?? new StubPowerShellRunner(_ => throw new InvalidOperationException("PowerShell execution was not expected.")),
            new NullLogger(),
            name => environment.TryGetValue(name, out var value) ? value : null,
            () => profile,
            downloadPackage ?? ((_, _, _) => throw new InvalidOperationException("Download was not expected.")),
            isWindows: () => true);
    }

    private static void CreateCredentialProviderPackage(string packagePath, string runtimeFolder, string providerFileName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"plugins/{runtimeFolder}/CredentialProvider.Microsoft/{providerFileName}");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("test credential provider");
    }

    private static string CreateArtefactsModulePackage(
        string modulePath,
        string runtimeFolder,
        string providerFileName,
        string moduleVersion = "1.0.0")
    {
        var artefactRoot = Path.Combine(
            modulePath,
            "PSPublishModule.Artefacts",
            moduleVersion,
            "Artefacts",
            "AzureArtifactsCredentialProvider");
        Directory.CreateDirectory(artefactRoot);
        var packagePath = Path.Combine(artefactRoot, $"Microsoft.{runtimeFolder}.NuGet.CredentialProvider.zip");
        CreateCredentialProviderPackage(packagePath, runtimeFolder, providerFileName);
        var sha256 = ComputeSha256(packagePath);
        var manifest = $$"""
{
  "name": "AzureArtifactsCredentialProvider",
  "version": "test",
  "files": [
    {
      "runtime": "{{runtimeFolder}}",
      "path": "{{Path.GetFileName(packagePath)}}",
      "sha256": "{{sha256}}"
    }
  ]
}
""";
        File.WriteAllText(Path.Combine(artefactRoot, "manifest.json"), manifest);
        return packagePath;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string CredentialProviderRelativePath(string runtime, string fileName)
        => Path.Combine(runtime, "CredentialProvider.Microsoft", fileName);

    private static string CredentialProviderPath(string profile, string runtime, string fileName)
        => Path.Combine(profile, ".nuget", "plugins", runtime, "CredentialProvider.Microsoft", fileName);

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

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _run;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> run)
        {
            _run = run;
        }

        public int CallCount { get; private set; }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            CallCount++;
            return _run(request);
        }
    }
}
