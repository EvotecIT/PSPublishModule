using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed class PublishVerificationHostServiceTests
{
    [Fact]
    public void PackageIdentityReader_UsesArchiveMetadataInsteadOfFileName()
    {
        using var package = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        var renamed = Path.Combine(package.RootPath, "signed-output.nupkg");
        File.Move(package.PackagePath, renamed);

        var identity = NuGetPackageIdentityReader.TryRead(renamed);

        Assert.NotNull(identity);
        Assert.Equal("Contoso.ReleaseOps", identity.Id);
        Assert.Equal("1.2.3", identity.Version);
    }

    [Fact]
    public async Task VerifyAsync_NuGetFeed_VerifiesPackageAgainstResolvedFlatContainer()
    {
        using var packageScope = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        using var client = new HttpClient(new StubHttpMessageHandler(request => CreateResponse(request.RequestUri)));
        using var service = new PublishVerificationHostService(
            client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => new PowerShellRunResult(1, string.Empty, string.Empty, "pwsh"))),
            new ModuleManifestMetadataReader());

        var result = await service.VerifyAsync(new PublishVerificationRequest {
            RootPath = packageScope.RootPath,
            RepositoryName = "Contoso.ReleaseOps",
            AdapterKind = "ProjectBuild",
            TargetName = "Contoso.ReleaseOps.1.2.3.nupkg",
            TargetKind = "NuGet",
            Destination = "https://packages.contoso.test/nuget/v3/index.json",
            SourcePath = packageScope.PackagePath
        });

        Assert.Equal(PublishVerificationStatus.Verified, result.Status);
        Assert.Contains("packages.contoso.test", result.Summary);
    }

    [Fact]
    public async Task VerifyAsync_NuGetFeed_UsesSavedIdentityWhenLocalPackageWasRemoved()
    {
        using var packageScope = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        File.Delete(packageScope.PackagePath);
        using var client = new HttpClient(new StubHttpMessageHandler(request => CreateResponse(request.RequestUri)));
        using var service = new PublishVerificationHostService(
            client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => new PowerShellRunResult(1, string.Empty, string.Empty, "pwsh"))),
            new ModuleManifestMetadataReader());

        var result = await service.VerifyAsync(new PublishVerificationRequest {
            RootPath = packageScope.RootPath,
            RepositoryName = "Contoso.ReleaseOps",
            AdapterKind = "ProjectBuild",
            TargetName = "Contoso.ReleaseOps.1.2.3.nupkg",
            TargetKind = "NuGet",
            Destination = "https://packages.contoso.test/nuget/v3/index.json",
            SourcePath = packageScope.PackagePath,
            PackageId = "Contoso.ReleaseOps",
            PackageVersion = "1.2.3"
        });

        Assert.Equal(PublishVerificationStatus.Verified, result.Status);
        Assert.Contains("saved publication identity", result.Summary);
    }

    [Fact]
    public async Task VerifyAsync_NuGetFeed_RejectsArtifactDriftFromSavedIdentity()
    {
        using var packageScope = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        using var client = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Unexpected remote request")));
        using var service = new PublishVerificationHostService(
            client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => new PowerShellRunResult(1, string.Empty, string.Empty, "pwsh"))),
            new ModuleManifestMetadataReader());

        var result = await service.VerifyAsync(new PublishVerificationRequest {
            RootPath = packageScope.RootPath,
            RepositoryName = "Contoso.ReleaseOps",
            AdapterKind = "ProjectBuild",
            TargetName = "Contoso.ReleaseOps.1.2.3.nupkg",
            TargetKind = "NuGet",
            Destination = "https://packages.contoso.test/nuget/v3/index.json",
            SourcePath = packageScope.PackagePath,
            PackageId = "Other.Package",
            PackageVersion = "1.2.3"
        });

        Assert.Equal(PublishVerificationStatus.Failed, result.Status);
        Assert.Contains("differs from the saved", result.Summary);
    }

    [Fact]
    public async Task VerifyAsync_LocalNuGetFeed_ConfirmsCopiedBytesAndRejectsDrift()
    {
        using var packageScope = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        var feed = Directory.CreateDirectory(Path.Combine(packageScope.RootPath, "feed")).FullName;
        var publishedPath = Path.Combine(feed, Path.GetFileName(packageScope.PackagePath));
        File.Copy(packageScope.PackagePath, publishedPath);
        var approvedDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packageScope.PackagePath)));
        using var service = new PublishVerificationHostService();
        var request = new PublishVerificationRequest {
            RootPath = packageScope.RootPath,
            RepositoryName = "Contoso.ReleaseOps",
            AdapterKind = "ProjectBuild",
            TargetName = Path.GetFileName(packageScope.PackagePath),
            TargetKind = "NuGet",
            Destination = feed,
            SourcePath = packageScope.PackagePath,
            PackageId = "Contoso.ReleaseOps",
            PackageVersion = "1.2.3",
            ExpectedContentSha256 = approvedDigest
        };

        var matching = await service.VerifyAsync(request);
        Assert.Equal(PublishVerificationStatus.Verified, matching.Status);
        Assert.Contains("bytes match", matching.Summary);

        using (var archive = ZipFile.Open(publishedPath, ZipArchiveMode.Update))
            archive.CreateEntry("changed-after-publish.txt");
        var changed = await service.VerifyAsync(request);
        Assert.Equal(PublishVerificationStatus.Failed, changed.Status);
        Assert.Contains("bytes differ", changed.Summary);

        using (var archive = ZipFile.Open(packageScope.PackagePath, ZipArchiveMode.Update))
            archive.CreateEntry("changed-local-artifact.txt");
        var changedSource = await service.VerifyAsync(request);
        Assert.Equal(PublishVerificationStatus.Failed, changedSource.Status);
        Assert.Contains("approved digest", changedSource.Summary);
    }

    [Fact]
    public async Task VerifyAsync_LocalNuGetFileUri_UsesApprovedDigestAfterSourceRemoval()
    {
        using var packageScope = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        var feed = Directory.CreateDirectory(Path.Combine(packageScope.RootPath, "feed")).FullName;
        var publishedPath = Path.Combine(feed, Path.GetFileName(packageScope.PackagePath));
        File.Copy(packageScope.PackagePath, publishedPath);
        var approvedDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packageScope.PackagePath)));
        File.Delete(packageScope.PackagePath);
        using var service = new PublishVerificationHostService();
        var request = new PublishVerificationRequest {
            RootPath = packageScope.RootPath,
            RepositoryName = "Contoso.ReleaseOps",
            AdapterKind = "ProjectBuild",
            TargetName = Path.GetFileName(publishedPath),
            TargetKind = "NuGet",
            Destination = new Uri(feed).AbsoluteUri,
            SourcePath = packageScope.PackagePath,
            PackageId = "Contoso.ReleaseOps",
            PackageVersion = "1.2.3",
            ExpectedContentSha256 = approvedDigest
        };

        var delivered = await service.VerifyAsync(request);
        Assert.Equal(PublishVerificationStatus.Verified, delivered.Status);
        Assert.Contains("digest", delivered.Summary);

        using (var archive = ZipFile.Open(publishedPath, ZipArchiveMode.Update))
            archive.CreateEntry("replaced-package.txt");
        var replaced = await service.VerifyAsync(request);
        Assert.Equal(PublishVerificationStatus.Failed, replaced.Status);
        Assert.Contains("bytes differ", replaced.Summary);

        var noDigest = await service.VerifyAsync(new PublishVerificationRequest {
            RootPath = request.RootPath,
            RepositoryName = request.RepositoryName,
            AdapterKind = request.AdapterKind,
            TargetName = request.TargetName,
            TargetKind = request.TargetKind,
            Destination = request.Destination,
            SourcePath = request.SourcePath,
            PackageId = request.PackageId,
            PackageVersion = request.PackageVersion
        });
        Assert.Equal(PublishVerificationStatus.Failed, noDigest.Status);
        Assert.Contains("no approved digest", noDigest.Summary);

        File.Delete(publishedPath);
        var missing = await service.VerifyAsync(request);
        Assert.Equal(PublishVerificationStatus.Failed, missing.Status);
        Assert.Contains("does not contain", missing.Summary);
    }

    [Fact]
    public async Task VerifyAsync_RelativeLocalNuGetFeed_UsesPackageDirectory()
    {
        using var packageScope = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        var feed = Directory.CreateDirectory(Path.Combine(packageScope.RootPath, "feed")).FullName;
        File.Copy(packageScope.PackagePath, Path.Combine(feed, Path.GetFileName(packageScope.PackagePath)));
        using var service = new PublishVerificationHostService();

        var result = await service.VerifyAsync(new PublishVerificationRequest {
            RootPath = packageScope.RootPath,
            RepositoryName = "Contoso.ReleaseOps",
            AdapterKind = "ProjectBuild",
            TargetName = Path.GetFileName(packageScope.PackagePath),
            TargetKind = "NuGet",
            Destination = "." + Path.DirectorySeparatorChar + "feed",
            SourcePath = packageScope.PackagePath,
            PackageId = "Contoso.ReleaseOps",
            PackageVersion = "1.2.3"
        });

        Assert.Equal(PublishVerificationStatus.Verified, result.Status);
        Assert.Contains("bytes match", result.Summary);
    }

    [Fact]
    public async Task VerifyAsync_NuGetFeed_RejectsHtmlLoginPageAtPackageEndpoint()
    {
        using var packageScope = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        File.Delete(packageScope.PackagePath);
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri?.AbsolutePath.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase) == true
                ? CreateResponse(request.RequestUri)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Sign in</html>") }));
        using var service = new PublishVerificationHostService(
            client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => new PowerShellRunResult(1, string.Empty, string.Empty, "pwsh"))),
            new ModuleManifestMetadataReader());

        var result = await service.VerifyAsync(new PublishVerificationRequest {
            RootPath = packageScope.RootPath,
            RepositoryName = "Contoso.ReleaseOps",
            AdapterKind = "ProjectBuild",
            TargetName = "Contoso.ReleaseOps.1.2.3.nupkg",
            TargetKind = "NuGet",
            Destination = "https://packages.contoso.test/nuget/v3/index.json",
            SourcePath = packageScope.PackagePath,
            PackageId = "Contoso.ReleaseOps",
            PackageVersion = "1.2.3"
        });

        Assert.Equal(PublishVerificationStatus.Failed, result.Status);
    }

    [Fact]
    public async Task VerifyAsync_PowerShellRepository_UsesSharedRepositoryResolverAndManifestReader()
    {
        using var moduleScope = CreateTemporaryModule("ContosoModule", "2.5.0", "preview1");
        using var client = new HttpClient(new StubHttpMessageHandler(request => CreateResponse(request.RequestUri)));
        using var service = new PublishVerificationHostService(
            client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(request => {
                if (request.CommandText is not null && request.CommandText.Contains("Get-PSResourceRepository", StringComparison.Ordinal))
                {
                    return new PowerShellRunResult(
                        0,
                        "{\"Name\":\"PrivateGallery\",\"SourceUri\":\"https://packages.contoso.test/powershell/v3/index.json\",\"PublishUri\":\"https://packages.contoso.test/powershell/api/v2/package\"}",
                        string.Empty,
                        "pwsh");
                }

                return new PowerShellRunResult(1, string.Empty, "Unexpected script", "pwsh");
            })),
            new ModuleManifestMetadataReader());

        var result = await service.VerifyAsync(new PublishVerificationRequest {
            RootPath = moduleScope.RootPath,
            RepositoryName = "ContosoModule",
            AdapterKind = "ModuleBuild",
            TargetName = "ContosoModule",
            TargetKind = "PowerShellRepository",
            Destination = "PrivateGallery",
            SourcePath = moduleScope.ModuleRoot
        });

        Assert.Equal(PublishVerificationStatus.Verified, result.Status);
        Assert.Contains("packages.contoso.test", result.Summary);
        Assert.Contains("2.5.0-preview1", result.Summary);
    }

    private static HttpResponseMessage CreateResponse(Uri? requestUri)
    {
        var path = requestUri?.AbsolutePath ?? string.Empty;
        if (path.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("{\"resources\":[{\"@id\":\"https://packages.contoso.test/v3-flatcontainer/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}")
            };
        }

        if (path.Contains("/v3-flatcontainer/", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.PartialContent) {
                Content = new ByteArrayContent([0x50, 0x4B, 0x03, 0x04])
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static TemporaryPackageScope CreateTemporaryPackage(string packageId, string version)
    {
        var root = Path.Combine(Path.GetTempPath(), $"powerforge-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var packagePath = Path.Combine(root, $"{packageId}.{version}.nupkg");
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"{packageId}.nuspec");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write($"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{packageId}</id>
                <version>{version}</version>
              </metadata>
            </package>
            """);

        return new TemporaryPackageScope(root, packagePath);
    }

    private static TemporaryModuleScope CreateTemporaryModule(string moduleName, string version, string preRelease)
    {
        var root = Path.Combine(Path.GetTempPath(), $"powerforge-module-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, moduleName);
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(
            Path.Combine(moduleRoot, $"{moduleName}.psd1"),
            "@{" + Environment.NewLine +
            $"    RootModule = '{moduleName}.psm1'" + Environment.NewLine +
            $"    ModuleVersion = '{version}'" + Environment.NewLine +
            "    PrivateData = @{" + Environment.NewLine +
            "        PSData = @{" + Environment.NewLine +
            $"            Prerelease = '{preRelease}'" + Environment.NewLine +
            "        }" + Environment.NewLine +
            "    }" + Environment.NewLine +
            "}" + Environment.NewLine);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), "function Test-PowerForge { }");
        return new TemporaryModuleScope(root, moduleRoot);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _execute;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> execute)
        {
            _execute = execute;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _execute(request);
    }

    private sealed class TemporaryPackageScope(string rootPath, string packagePath) : IDisposable
    {
        public string RootPath { get; } = rootPath;
        public string PackagePath { get; } = packagePath;

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class TemporaryModuleScope(string rootPath, string moduleRoot) : IDisposable
    {
        public string RootPath { get; } = rootPath;
        public string ModuleRoot { get; } = moduleRoot;

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
