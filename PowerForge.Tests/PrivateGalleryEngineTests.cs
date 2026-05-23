using System.IO.Compression;
using System.Net;
using System.Text;
using PowerForge;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class PrivateGalleryEngineTests
{
    [Fact]
    public async Task AzureArtifactsClient_ParsesPackageInventory()
    {
        var queries = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            queries.Add(request.RequestUri!.Query);
            Assert.Contains("protocolType=NuGet", request.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains("includeAllVersions=true", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Equal("Bearer token-value", request.Headers.Authorization?.ToString());
            var isPrereleaseQuery = request.RequestUri.Query.Contains("isRelease=false", StringComparison.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    isPrereleaseQuery
                        ? """
                          {
                            "count": 1,
                            "value": [
                              {
                                "id": "package-guid",
                                "name": "PSPublishModule",
                                "protocolType": "NuGet",
                                "description": "Preview module",
                                "versions": [
                                  {
                                    "id": "preview-version-guid",
                                    "version": "1.0.0-preview1",
                                    "normalizedVersion": "1.0.0-preview1",
                                    "isLatest": true,
                                    "isListed": true,
                                    "isDeleted": false
                                  }
                                ]
                              }
                            ]
                          }
                          """
                        : """
                    {
                      "count": 1,
                      "value": [
                        {
                          "id": "package-guid",
                          "name": "PSPublishModule",
                          "protocolType": "NuGet",
                          "description": "Publishing module",
                          "_links": { "web": { "href": "https://dev.azure.com/org/project/_artifacts/feed/feed/package/PSPublishModule" } },
                          "versions": [
                            {
                              "id": "version-guid",
                              "version": "3.0.13",
                              "normalizedVersion": "3.0.13",
                              "isLatest": true,
                              "isListed": true,
                              "isDeleted": false,
                              "publishDate": "2026-05-23T10:11:12Z",
                              "description": "Current version",
                              "author": "Evotec",
                              "dependencies": [
                                { "packageName": "Pester", "versionRange": "[5.7.1, )", "group": "PowerShell" }
                              ],
                              "views": [
                                { "name": "@Release" }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var warnings = new List<string>();
        var client = new AzureArtifactsPrivateGalleryClient(handler);
        var packages = await client.GetPackagesAsync(new PrivateGalleryIndexOptions
        {
            Organization = "evotecpl",
            Project = "PowerShellGallery",
            Feed = "PowerShellGalleryFeed",
            Token = "token-value",
            AuthenticationKind = PrivateGalleryAuthenticationKind.Bearer,
            MaxPackages = 1
        }, warnings);

        Assert.Empty(warnings);
        Assert.Contains(queries, query => query.Contains("isRelease=true", StringComparison.Ordinal));
        Assert.Contains(queries, query => query.Contains("isRelease=false", StringComparison.Ordinal));
        Assert.Single(packages);
        var package = Assert.Single(packages, package => package.Name == "PSPublishModule");
        Assert.Equal("PSPublishModule", package.Name);
        Assert.Equal("3.0.13", package.LatestVersion);
        Assert.Equal("Publishing module", package.Description);
        Assert.Equal("https://dev.azure.com/org/project/_artifacts/feed/feed/package/PSPublishModule", package.WebUrl);

        Assert.Equal(2, package.Versions.Count);
        var version = Assert.Single(package.Versions, version => version.Version == "3.0.13");
        Assert.True(version.IsLatest);
        Assert.True(version.IsListed);
        Assert.False(version.IsDeleted);
        Assert.Equal("Evotec", version.Author);
        Assert.Equal("@Release", Assert.Single(version.Views));
        Assert.Equal("Pester", Assert.Single(version.Dependencies).Name);
        Assert.Contains(package.Versions, version => version.Version == "1.0.0-preview1");
    }

    [Fact]
    public async Task NuGetV3PackageDownloader_CachesPackageBaseAddress()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-private-gallery-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var serviceIndexRequests = 0;
        var packageRequests = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
            {
                serviceIndexRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "resources": [
                            { "@type": "PackageBaseAddress/3.0.0", "@id": "https://pkgs.example.test/flat/" }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            packageRequests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("package"))
            };
        });

        try
        {
            var downloader = new NuGetV3PackageDownloader(handler);
            var options = new PrivateGalleryIndexOptions();
            await downloader.DownloadPackageAsync("https://pkgs.example.test/index.json", "Contoso.Tools", "1.0.0", Path.Combine(root, "one.nupkg"), options);
            await downloader.DownloadPackageAsync("https://pkgs.example.test/index.json", "Contoso.Tools", "1.0.1", Path.Combine(root, "two.nupkg"), options);

            Assert.Equal(1, serviceIndexRequests);
            Assert.Equal(2, packageRequests);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task AzureArtifactsClient_ParsesPackageAndVersionMetrics()
    {
        var calls = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Basic OnRva2VuLXZhbHVl", request.Headers.Authorization?.ToString());

            if (request.RequestUri.AbsolutePath.EndsWith("/packagemetricsbatch", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        [
                          {
                            "packageId": "package-guid",
                            "downloadCount": 120,
                            "downloadUniqueUsers": 7,
                            "lastDownloaded": "2026-05-23T11:12:13Z"
                          }
                        ]
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    [
                      {
                        "packageId": "package-guid",
                        "packageVersionId": "version-guid",
                        "downloadCount": 12,
                        "downloadUniqueUsers": 3,
                        "lastDownloaded": "2026-05-23T12:13:14Z"
                      }
                    ]
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var client = new AzureArtifactsPrivateGalleryClient(handler);
        var options = new PrivateGalleryIndexOptions
        {
            Organization = "evotecpl",
            Project = "PowerShellGallery",
            Feed = "PowerShellGalleryFeed",
            Token = "token-value",
            AuthenticationKind = PrivateGalleryAuthenticationKind.BasicToken
        };

        var packageMetrics = await client.GetPackageMetricsAsync(options, new[] { "package-guid" });
        var versionMetrics = await client.GetPackageVersionMetricsAsync(options, "package-guid", new[] { "version-guid" });

        Assert.Equal(2, calls.Count);
        var packageMetric = packageMetrics["package-guid"];
        Assert.Equal(120, packageMetric.DownloadCount);
        Assert.Equal(7, packageMetric.UniqueUsers);
        Assert.Equal(DateTimeOffset.Parse("2026-05-23T11:12:13Z").ToUniversalTime(), packageMetric.LastDownloadedAtUtc);

        var versionMetric = versionMetrics["version-guid"];
        Assert.Equal(12, versionMetric.DownloadCount);
        Assert.Equal(3, versionMetric.UniqueUsers);
        Assert.Equal(DateTimeOffset.Parse("2026-05-23T12:13:14Z").ToUniversalTime(), versionMetric.LastDownloadedAtUtc);
    }

    [Fact]
    public void PowerShellModulePackageInspector_ReadsStaticModuleMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-private-gallery-inspect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var packagePath = Path.Combine(root, "Contoso.Tools.1.0.0.nupkg");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                AddEntry(archive, "Contoso.Tools/Contoso.Tools.psd1",
                    """
                    @{
                      RootModule = 'Contoso.Tools.psm1'
                      ModuleVersion = '1.0.0'
                      Author = 'Contoso'
                      CompanyName = 'Contoso Ltd'
                      Description = 'Internal tools'
                      PowerShellVersion = '7.2'
                      CompatiblePSEditions = @('Core')
                      FunctionsToExport = @('Get-ContosoTool')
                      CmdletsToExport = @('Set-ContosoTool')
                      RequiredModules = @(
                        @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }
                        'PSReadLine'
                      )
                      PrivateData = @{
                        PSData = @{
                          Tags = @('Internal','Tools')
                        }
                      }
                    }
                    """);

                AddEntry(archive, "Contoso.Tools/en-US/Contoso.Tools-help.xml",
                    """
                    <helpItems xmlns="http://msh">
                      <command:command xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
                        <command:details>
                          <command:name>Invoke-ContosoTool</command:name>
                        </command:details>
                        <command:description>
                          <maml:para xmlns:maml="http://schemas.microsoft.com/maml/2004/10">Runs a Contoso tool.</maml:para>
                        </command:description>
                        <command:syntax />
                        <command:parameters />
                        <command:inputTypes />
                        <command:returnValues />
                        <command:examples />
                        <command:relatedLinks />
                        <command:remarks />
                        <command:alertSet />
                        <command:keywords />
                        <command:summary>
                          <maml:para xmlns:maml="http://schemas.microsoft.com/maml/2004/10">Runs a Contoso tool.</maml:para>
                        </command:summary>
                        <command:synopsis>Runs a Contoso tool.</command:synopsis>
                      </command:command>
                    </helpItems>
                    """);
                AddEntry(archive, "Contoso.Tools/README.md", "# Contoso Tools");
                AddEntry(archive, "docs/GettingStarted.md", "# Getting Started");
                AddEntry(archive, "examples/Get-ContosoTool.ps1", "Get-ContosoTool");
                AddEntry(archive, "Contoso.Tools/LICENSE", "MIT");
            }

            var metadata = new PowerShellModulePackageInspector().Inspect(packagePath);

            Assert.Equal("Contoso.Tools", metadata.Name);
            Assert.Equal("1.0.0", metadata.Version);
            Assert.Equal("Internal tools", metadata.Description);
            Assert.Equal("Contoso", metadata.Author);
            Assert.Equal("Contoso Ltd", metadata.CompanyName);
            Assert.Equal("7.2", metadata.PowerShellVersion);
            Assert.Contains("Core", metadata.CompatiblePSEditions);
            Assert.Contains("Internal", metadata.Tags);

            Assert.Contains(metadata.Commands, command => command.Name == "Get-ContosoTool" && command.Kind == "Function");
            Assert.Contains(metadata.Commands, command => command.Name == "Set-ContosoTool" && command.Kind == "Cmdlet");
            Assert.Contains(metadata.Commands, command => command.Name == "Invoke-ContosoTool");
            Assert.Contains(metadata.RequiredModules, dependency => dependency.Name == "Pester" && dependency.VersionRange == "5.7.1");
            Assert.Contains(metadata.RequiredModules, dependency => dependency.Name == "PSReadLine");
            Assert.Contains(metadata.Documents, document => document.Kind == "readme");
            Assert.Contains(metadata.Documents, document => document.Kind == "docs");
            Assert.Contains(metadata.Documents, document => document.Kind == "example");
            Assert.Contains(metadata.Documents, document => document.Kind == "help");
            Assert.Contains(metadata.Documents, document => document.Kind == "license");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void WebPrivateGallerySearchBuilder_BuildsModuleCommandAndDocumentEntries()
    {
        var document = new PrivateGalleryDocument
        {
            Packages =
            {
                new PrivateGalleryPackage
                {
                    Name = "Contoso.Tools",
                    LatestVersion = "1.0.0",
                    Description = "Internal tools",
                    Versions =
                    {
                        new PrivateGalleryPackageVersion { Id = "v1", Version = "1.0.0", IsLatest = true }
                    },
                    Module = new PrivateGalleryModuleMetadata
                    {
                        Name = "Contoso.Tools",
                        Version = "1.0.0",
                        Tags = { "Internal" },
                        Commands =
                        {
                            new PrivateGalleryCommandMetadata { Name = "Get-ContosoTool", Kind = "Function", Synopsis = "Gets a tool." }
                        },
                        Documents =
                        {
                            new PrivateGalleryDocumentAsset { Path = "Contoso.Tools/README.md", Kind = "readme", Title = "README" }
                        }
                    }
                }
            }
        };

        var search = WebPrivateGallerySearchBuilder.Build(document);

        Assert.Contains(search.Entries, entry => entry.Kind == "module" && entry.Title == "Contoso.Tools");
        Assert.Contains(search.Entries, entry => entry.Kind == "version" && entry.Version == "1.0.0");
        Assert.Contains(search.Entries, entry => entry.Kind == "command" && entry.Title == "Get-ContosoTool" && entry.Summary == "Gets a tool.");
        Assert.Contains(search.Entries, entry => entry.Kind == "document" && entry.Title == "README");
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
