using System.Net;
using System.Net.Http;

namespace PowerForge.Tests;

public sealed class ManagedModuleRepositoryPublishTests
{
    [Fact]
    public async Task PublishPackageAsync_reports_repository_reason_and_redacts_api_key()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        using var client = new HttpClient(new PublishFailureHandler("publish-key"));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("PSGallery", ManagedModuleCatalogDefaults.PowerShellGalleryV2);

        var exception = await Assert.ThrowsAsync<ManagedModuleRepositoryException>(() =>
            repositoryClient.PublishPackageAsync(
                repository,
                packagePath,
                new RepositoryCredential { Secret = "publish-key" }));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("client version '4.1.0'", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("publish-key", exception.Message, StringComparison.Ordinal);
    }

    private sealed class PublishFailureHandler : HttpMessageHandler
    {
        private readonly string _apiKey;

        public PublishFailureHandler(string apiKey)
        {
            _apiKey = apiKey;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "A client version '4.1.0' or higher is required.",
                Content = new StringContent($"Rejected credential {_apiKey}")
            };
            return Task.FromResult(response);
        }
    }
}
