using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebEcosystemStatsGeneratorTests
{
    [Fact]
    public void Generate_CollectsGitHubNuGetAndPowerShellGalleryStats()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-ecosystem-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outPath = Path.Combine(root, "data", "ecosystem-stats.json");
            var handler = new FakeEcosystemStatsHandler();

            var result = WebEcosystemStatsGenerator.Generate(new WebEcosystemStatsOptions
            {
                OutputPath = outPath,
                GitHubOrganization = "EvotecIT",
                NuGetOwner = "Evotec",
                PowerShellGalleryOwner = "Przemyslaw.Klys",
                PowerShellGalleryAuthor = "Przemyslaw Klys",
                MaxItems = 10
            }, handler);

            Assert.Equal(outPath, result.OutputPath);
            Assert.Equal(2, result.RepositoryCount);
            Assert.Equal(2, result.NuGetPackageCount);
            Assert.Equal(2, result.PowerShellGalleryModuleCount);
            Assert.Empty(result.Warnings);
            Assert.True(File.Exists(outPath));

            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            var rootElement = document.RootElement;
            var summary = rootElement.GetProperty("summary");
            Assert.Equal(2, summary.GetProperty("repositoryCount").GetInt32());
            Assert.Equal(2, summary.GetProperty("nuGetPackageCount").GetInt32());
            Assert.Equal(2, summary.GetProperty("powerShellGalleryModuleCount").GetInt32());
            Assert.Equal(7000L, summary.GetProperty("nuGetDownloads").GetInt64());
            Assert.Equal(1500L, summary.GetProperty("powerShellGalleryDownloads").GetInt64());
            Assert.Equal(8500L, summary.GetProperty("totalDownloads").GetInt64());

            var gitHub = rootElement.GetProperty("gitHub");
            Assert.Equal("EvotecIT", gitHub.GetProperty("organization").GetString());
            Assert.Equal(2, gitHub.GetProperty("repositoryCount").GetInt32());

            var nuget = rootElement.GetProperty("nuget");
            Assert.Equal("Evotec", nuget.GetProperty("owner").GetString());
            Assert.Equal(2, nuget.GetProperty("packageCount").GetInt32());

            var powerShellGallery = rootElement.GetProperty("powerShellGallery");
            Assert.Equal("Przemyslaw.Klys", powerShellGallery.GetProperty("owner").GetString());
            Assert.Equal(2, powerShellGallery.GetProperty("moduleCount").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_GitHubForbiddenWithToken_RetriesWithoutAuthorizationForPublicOrg()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-ecosystem-stats-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outPath = Path.Combine(root, "data", "ecosystem-stats.json");
            var handler = new GitHubTokenFallbackHandler();

            var result = WebEcosystemStatsGenerator.Generate(new WebEcosystemStatsOptions
            {
                OutputPath = outPath,
                GitHubOrganization = "EvotecIT",
                GitHubToken = "test-token",
                MaxItems = 10
            }, handler);

            Assert.Equal(outPath, result.OutputPath);
            Assert.Equal(1, result.RepositoryCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("retried anonymously", StringComparison.OrdinalIgnoreCase));
            Assert.True(handler.SawAuthorizedForbidden);
            Assert.True(handler.SawAnonymousSuccess);
            Assert.True(File.Exists(outPath));

            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            var summary = document.RootElement.GetProperty("summary");
            Assert.Equal(1, summary.GetProperty("repositoryCount").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
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
            // ignore cleanup failures in tests
        }
    }

    private sealed class FakeEcosystemStatsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("missing uri", Encoding.UTF8, "text/plain")
                });
            }

            if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                var page = ParseQuery(uri, "page", fallback: 1);
                if (page == 1)
                {
                    const string githubPageOne = """
                    [
                      {
                        "name":"ADEssentials",
                        "full_name":"EvotecIT/ADEssentials",
                        "html_url":"https://github.com/EvotecIT/ADEssentials",
                        "language":"PowerShell",
                        "archived":false,
                        "stargazers_count":10,
                        "forks_count":4,
                        "watchers_count":10,
                        "open_issues_count":1,
                        "pushed_at":"2026-02-20T10:00:00Z"
                      },
                      {
                        "name":"PSWriteHTML",
                        "full_name":"EvotecIT/PSWriteHTML",
                        "html_url":"https://github.com/EvotecIT/PSWriteHTML",
                        "language":"PowerShell",
                        "archived":false,
                        "stargazers_count":20,
                        "forks_count":8,
                        "watchers_count":20,
                        "open_issues_count":2,
                        "pushed_at":"2026-02-21T11:00:00Z"
                      }
                    ]
                    """;
                    return JsonResponse(githubPageOne);
                }

                return JsonResponse("[]");
            }

            if (uri.Host.Equals("api-v2v3search-0.nuget.org", StringComparison.OrdinalIgnoreCase))
            {
                const string nugetPayload = """
                {
                  "totalHits": 2,
                  "data": [
                    {
                      "id":"OfficeIMO.Word",
                      "version":"1.0.32",
                      "totalDownloads":5000,
                      "packageUrl":"https://api.nuget.org/v3/registration5-gz-semver2/officeimo.word/index.json",
                      "projectUrl":"https://github.com/EvotecIT/OfficeIMO",
                      "description":"Word generation library",
                      "verified":true
                    },
                    {
                      "id":"CodeGlyphX",
                      "version":"1.2.0",
                      "totalDownloads":2000,
                      "packageUrl":"https://api.nuget.org/v3/registration5-gz-semver2/codeglyphx/index.json",
                      "projectUrl":"https://codeglyphx.com",
                      "description":"QR and barcode toolkit",
                      "verified":true
                    }
                  ]
                }
                """;
                return JsonResponse(nugetPayload);
            }

            if (uri.Host.Equals("www.powershellgallery.com", StringComparison.OrdinalIgnoreCase))
            {
                var skip = ParseQuery(uri, "$skip", fallback: 0);
                if (skip == 0)
                {
                    const string psGalleryFeed = """
                    <?xml version="1.0" encoding="utf-8"?>
                    <feed xml:base="https://www.powershellgallery.com/api/v2"
                          xmlns="http://www.w3.org/2005/Atom"
                          xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                          xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                      <id>https://www.powershellgallery.com/api/v2/Packages</id>
                      <title />
                      <updated>2026-03-03T10:00:00Z</updated>
                      <entry>
                        <id>https://www.powershellgallery.com/api/v2/Packages(Id='PSWriteHTML',Version='1.0.0')</id>
                        <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/PSWriteHTML/1.0.0" />
                        <m:properties>
                          <d:Id>PSWriteHTML</d:Id>
                          <d:Version>1.0.0</d:Version>
                          <d:Authors>Przemyslaw Klys</d:Authors>
                          <d:Owners>Przemyslaw.Klys</d:Owners>
                          <d:DownloadCount m:type="Edm.Int64">1000</d:DownloadCount>
                          <d:GalleryDetailsUrl>https://www.powershellgallery.com/packages/PSWriteHTML/1.0.0</d:GalleryDetailsUrl>
                          <d:ProjectUrl>https://github.com/EvotecIT/PSWriteHTML</d:ProjectUrl>
                          <d:Description>HTML reports</d:Description>
                        </m:properties>
                      </entry>
                      <entry>
                        <id>https://www.powershellgallery.com/api/v2/Packages(Id='ADEssentials',Version='0.9.0')</id>
                        <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/ADEssentials/0.9.0" />
                        <m:properties>
                          <d:Id>ADEssentials</d:Id>
                          <d:Version>0.9.0</d:Version>
                          <d:Authors>Przemyslaw Klys</d:Authors>
                          <d:Owners>Przemyslaw.Klys</d:Owners>
                          <d:DownloadCount m:type="Edm.Int64">500</d:DownloadCount>
                          <d:GalleryDetailsUrl>https://www.powershellgallery.com/packages/ADEssentials/0.9.0</d:GalleryDetailsUrl>
                          <d:ProjectUrl>https://github.com/EvotecIT/ADEssentials</d:ProjectUrl>
                          <d:Description>Active Directory helpers</d:Description>
                        </m:properties>
                      </entry>
                    </feed>
                    """;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(psGalleryFeed, Encoding.UTF8, "application/atom+xml")
                    });
                }

                const string emptyFeed = """
                <?xml version="1.0" encoding="utf-8"?>
                <feed xml:base="https://www.powershellgallery.com/api/v2"
                      xmlns="http://www.w3.org/2005/Atom"
                      xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                      xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                  <id>https://www.powershellgallery.com/api/v2/Packages</id>
                  <title />
                  <updated>2026-03-03T10:00:00Z</updated>
                </feed>
                """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(emptyFeed, Encoding.UTF8, "application/atom+xml")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }

        private static Task<HttpResponseMessage> JsonResponse(string json)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        private static int ParseQuery(Uri uri, string key, int fallback)
        {
            var query = uri.Query?.TrimStart('?');
            if (string.IsNullOrWhiteSpace(query))
                return fallback;

            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = pair.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex >= pair.Length - 1)
                    continue;
                var parsedKey = Uri.UnescapeDataString(pair[..separatorIndex]);
                if (!parsedKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                var parsedValue = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
                return int.TryParse(parsedValue, out var parsed) ? parsed : fallback;
            }

            return fallback;
        }
    }

    private sealed class GitHubTokenFallbackHandler : HttpMessageHandler
    {
        public bool SawAuthorizedForbidden { get; private set; }
        public bool SawAnonymousSuccess { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("missing uri", Encoding.UTF8, "text/plain")
                });
            }

            if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                var page = ParseQuery(uri, "page", fallback: 1);
                if (page == 1 && request.Headers.Authorization is not null)
                {
                    SawAuthorizedForbidden = true;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                    {
                        Content = new StringContent("forbidden", Encoding.UTF8, "text/plain")
                    });
                }

                SawAnonymousSuccess = true;
                const string githubPayload = """
                [
                  {
                    "name":"PSWriteHTML",
                    "full_name":"EvotecIT/PSWriteHTML",
                    "html_url":"https://github.com/EvotecIT/PSWriteHTML",
                    "language":"PowerShell",
                    "archived":false,
                    "stargazers_count":10,
                    "forks_count":2,
                    "watchers_count":10,
                    "open_issues_count":1,
                    "pushed_at":"2026-03-04T10:00:00Z"
                  }
                ]
                """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(githubPayload, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }

        private static int ParseQuery(Uri uri, string key, int fallback)
        {
            var query = uri.Query?.TrimStart('?');
            if (string.IsNullOrWhiteSpace(query))
                return fallback;

            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = pair.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex >= pair.Length - 1)
                    continue;
                var parsedKey = Uri.UnescapeDataString(pair[..separatorIndex]);
                if (!parsedKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                var parsedValue = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
                return int.TryParse(parsedValue, out var parsed) ? parsed : fallback;
            }

            return fallback;
        }
    }
}
