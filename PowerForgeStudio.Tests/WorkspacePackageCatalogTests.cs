using System.Net;
using System.Text;
using PowerForgeStudio.Orchestrator.Packages;

namespace PowerForgeStudio.Tests;

public sealed class WorkspacePackageCatalogTests
{
    [Fact]
    public async Task ReadsBothPublicRegistriesAndPreservesWarnings()
    {
        using var client = Client("""
            {
              "generatedAtUtc":"2026-09-22T08:52:35Z",
              "summary":{"nuGetDownloads":1200,"powerShellGalleryDownloads":300},
              "warnings":["A registry read was partial"],
              "nuget":{"packages":[
                {"id":"OfficeIMO.Word","version":"1.2.3","totalDownloads":1200},
                {"id":"bad/id","version":"0.1.0","totalDownloads":1}
              ]},
              "powerShellGallery":{"modules":[
                {"id":"PSPublishModule","version":"3.0.144","downloadCount":300}
              ]}
            }
            """);

        var snapshot = await new WorkspacePackageCatalogService(client).ReadAsync();

        Assert.Equal(2, snapshot.TotalCount);
        Assert.Equal(1, snapshot.NuGetCount);
        Assert.Equal(1, snapshot.PowerShellGalleryCount);
        Assert.Equal(1500, snapshot.TotalDownloads);
        Assert.True(snapshot.IsPartial);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("invalid or duplicated", StringComparison.Ordinal));
        Assert.Equal("https://www.nuget.org/packages/OfficeIMO.Word", snapshot.Packages[0].DetailsUrl);
        Assert.Equal("https://www.powershellgallery.com/packages/PSPublishModule", snapshot.Packages[1].DetailsUrl);
    }

    [Fact]
    public async Task RejectsMissingGenerationTimeAndOversizePayload()
    {
        using var missingTime = Client("""{"nuget":{"packages":[{"id":"Valid"}]}}""");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new WorkspacePackageCatalogService(missingTime).ReadAsync());

        using var oversized = Client(new string('x', 2 * 1024 * 1024 + 1));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new WorkspacePackageCatalogService(oversized).ReadAsync());
    }

    [Fact]
    public async Task MarksMissingRegistrySectionAsPartial()
    {
        using var client = Client("""
            {"generatedAtUtc":"2026-09-22T08:52:35Z",
             "summary":{"nuGetDownloads":10,"powerShellGalleryDownloads":0},
             "nuget":{"packages":[{"id":"OfficeIMO.Word","totalDownloads":10}]}}
            """);

        var snapshot = await new WorkspacePackageCatalogService(client).ReadAsync();

        Assert.Single(snapshot.Packages);
        Assert.True(snapshot.IsPartial);
        Assert.Equal(1, snapshot.WarningCount);
        Assert.Null(snapshot.PowerShellGalleryDownloads);
        Assert.False(snapshot.PowerShellGalleryAvailable);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("Gallery unavailable", StringComparison.Ordinal));
        Assert.Null(snapshot.TotalDownloads);
    }

    [Fact]
    public async Task FailedEmptyRegistryDoesNotClaimZeroDownloads()
    {
        using var client = Client("""
            {"generatedAtUtc":"2026-09-22T08:52:35Z",
             "summary":{"nuGetDownloads":10,"powerShellGalleryDownloads":0},
             "warnings":["PowerShell Gallery request failed: TaskCanceledException"],
             "nuget":{"packages":[{"id":"OfficeIMO.Word","totalDownloads":10}]},
             "powerShellGallery":{"modules":[]}}
            """);

        var snapshot = await new WorkspacePackageCatalogService(client).ReadAsync();

        Assert.False(snapshot.PowerShellGalleryAvailable);
        Assert.Null(snapshot.PowerShellGalleryDownloads);
        Assert.Null(snapshot.TotalDownloads);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("Gallery unavailable", StringComparison.Ordinal));
    }

    private static HttpClient Client(string content) => new(new StubHandler(content));

    private sealed class StubHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(WorkspacePackageCatalogService.SourceUrl, request.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
