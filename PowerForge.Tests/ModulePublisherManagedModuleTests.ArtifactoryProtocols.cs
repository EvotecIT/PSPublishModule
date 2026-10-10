using System.Net;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class ModulePublisherManagedModuleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Split_feed_version_preflight_uses_the_artifactory_read_protocol(bool v3)
    {
        using var root = new TemporaryDirectory();
        const string baseUri = "https://company.jfrog.io/artifactory/api/nuget/";
        var source = baseUri + (v3 ? "v3/virtual/index.json" : "virtual");
        using var handler = new ArtifactoryVersionHandler(source, v3);
        using var client = new HttpClient(handler);
        var publisher = new ModulePublisher(new NullLogger(), new StubPowerShellRunner(_ =>
            throw new Exception("Split-feed preflight must not change repository registrations.")), client);
        var publish = DependencyPublish(source, baseUri + "v3/local/index.json", false);
        publish.Tool = PublishTool.PSResourceGet;

        var result = publisher.ValidateVersionForPublish(publish, CreatePlan(root.Path), allowExistingExactVersion: true);

        Assert.Equal(ModulePublishVersionPreflightResult.AlreadyPublished, result);
        Assert.Equal(v3 ? 2 : 1, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.DoesNotContain("/local/", uri));
    }

    private sealed class ArtifactoryVersionHandler(string source, bool v3) : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.AbsoluteUri;
            Requests.Add(uri);
            string body;
            if (v3 && uri == source)
                body = """{"resources":[{"@id":"https://company.jfrog.io/artifactory/api/nuget/v3/virtual/flat/","@type":"PackageBaseAddress/3.0.0"}]}""";
            else if (v3 && uri == "https://company.jfrog.io/artifactory/api/nuget/v3/virtual/flat/pspublishmodule/index.json")
                body = """{"versions":["3.0.13"]}""";
            else if (!v3 && uri == source + "/FindPackagesById()?id='PSPublishModule'&semVerLevel=2.0.0")
                body = """<feed xmlns="http://www.w3.org/2005/Atom" xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"><entry><content><m:properties xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"><d:Version>3.0.13</d:Version></m:properties></content></entry></feed>""";
            else
                throw new Exception("Unexpected Artifactory version query: " + uri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, v3 ? "application/json" : "application/atom+xml")
            });
        }
    }
}
