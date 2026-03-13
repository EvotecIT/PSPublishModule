using System.IO.Compression;
using System.Net;
using System.Net.Http;

namespace PowerForge.Tests;

public sealed class PublishVerificationHostServiceTests
{
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
            return new HttpResponseMessage(HttpStatusCode.OK);
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
