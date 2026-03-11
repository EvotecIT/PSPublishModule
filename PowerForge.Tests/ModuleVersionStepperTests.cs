using System.Net;
using System.Net.Http;
using System.Text;

namespace PowerForge.Tests;

public sealed class ModuleVersionStepperTests
{
    [Fact]
    public void Step_UsesUnlistedPowerShellGalleryVersionWhenStepping()
    {
        using var client = new HttpClient(new FakePowerShellGalleryFeedHandler());
        var stepper = new ModuleVersionStepper(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "2.0.27"), string.Empty, "pwsh.exe")),
            client);

        var result = stepper.Step("3.0.X", moduleName: "PSPublishModule", localPsd1Path: null, repository: "PSGallery");

        Assert.Equal("3.0.1", result.Version);
        Assert.Equal(ModuleVersionSource.Repository, result.CurrentVersionSource);
        Assert.Equal("3.0.0", result.CurrentVersion);
    }

    private static string VisibleRepositoryItem(string name, string version)
        => string.Join("::", new[]
        {
            "PFPSRG::ITEM",
            Encode(name),
            Encode(version),
            Encode("PSGallery"),
            Encode("Przemyslaw Klys"),
            Encode(name),
            Encode(Guid.Empty.ToString()),
            Encode(string.Empty)
        });

    private static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly PowerShellRunResult _result;

        public StubPowerShellRunner(PowerShellRunResult result)
        {
            _result = result;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            return _result;
        }
    }

    private sealed class FakePowerShellGalleryFeedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            var body = uri.Contains("$skip=100", StringComparison.OrdinalIgnoreCase)
                ? BuildSecondPage()
                : BuildFirstPage();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/atom+xml")
            });
        }

        private static string BuildFirstPage()
            => """
               <?xml version="1.0" encoding="utf-8"?>
               <feed xmlns="http://www.w3.org/2005/Atom"
                     xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                     xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                 <entry>
                   <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/PSPublishModule/2.0.27" />
                   <m:properties>
                     <d:Version>2.0.27</d:Version>
                     <d:IsPrerelease>false</d:IsPrerelease>
                     <d:Published m:type="Edm.DateTime">2026-03-10T10:00:00</d:Published>
                   </m:properties>
                 </entry>
                 <link rel="next" href="https://www.powershellgallery.com/api/v2/FindPackagesById()?id=%27PSPublishModule%27&amp;$skip=100" />
               </feed>
               """;

        private static string BuildSecondPage()
            => """
               <?xml version="1.0" encoding="utf-8"?>
               <feed xmlns="http://www.w3.org/2005/Atom"
                     xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                     xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                 <entry>
                   <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/PSPublishModule/3.0.0" />
                   <m:properties>
                     <d:Version>3.0.0</d:Version>
                     <d:IsPrerelease>false</d:IsPrerelease>
                     <d:Published m:type="Edm.DateTime">1900-01-01T00:00:00</d:Published>
                   </m:properties>
                 </entry>
               </feed>
               """;
    }
}
