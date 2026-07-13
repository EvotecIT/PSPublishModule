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

    [Fact]
    public void Step_UsesReservedPowerShellGalleryVersionWhenFeedOmitsIt()
    {
        using var client = new HttpClient(new FakeReservedPowerShellGalleryHandler());
        var stepper = new ModuleVersionStepper(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "2.0.27"), string.Empty, "pwsh.exe")),
            client);

        var result = stepper.Step("3.0.X", moduleName: "PSPublishModule", localPsd1Path: null, repository: "PSGallery");

        Assert.Equal("3.0.1", result.Version);
        Assert.Equal(ModuleVersionSource.Repository, result.CurrentVersionSource);
        Assert.Equal("3.0.0", result.CurrentVersion);
    }

    [Fact]
    public void Step_ConfirmsCandidateVersionIsFree_WhenRepositoryLookupReturnsNothing()
    {
        using var client = new HttpClient(new FakeCandidateReservationHandler("3.0.0"));
        var stepper = new ModuleVersionStepper(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe")),
            client);

        var result = stepper.Step("3.0.X", moduleName: "PSPublishModule", localPsd1Path: null, repository: "PSGallery");

        Assert.Equal("3.0.1", result.Version);
        Assert.Equal(ModuleVersionSource.Repository, result.CurrentVersionSource);
        Assert.Equal("3.0.0", result.CurrentVersion);
    }

    [Fact]
    public void Step_ContinuesBumpingUntilExactCandidateIsFree()
    {
        using var client = new HttpClient(new FakeCandidateReservationHandler("3.0.0", "3.0.1"));
        var stepper = new ModuleVersionStepper(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe")),
            client);

        var result = stepper.Step("3.0.X", moduleName: "PSPublishModule", localPsd1Path: null, repository: "PSGallery");

        Assert.Equal("3.0.2", result.Version);
        Assert.Equal(ModuleVersionSource.Repository, result.CurrentVersionSource);
        Assert.Equal("3.0.1", result.CurrentVersion);
    }

    [Fact]
    public void Step_UsesGalleryBaselineWhenTheRawFeedConfirmsNoPublishedVersions()
    {
        using var client = new HttpClient(new FakeCandidateReservationHandler());
        var stepper = new ModuleVersionStepper(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(1, string.Empty, "Find-PSResource reported no matching package.", "pwsh.exe")),
            client);

        var result = stepper.Step("3.0.X", moduleName: "PSPublishModule", localPsd1Path: null, repository: "PSGallery");

        Assert.Equal("3.0.0", result.Version);
        Assert.Equal(ModuleVersionSource.Repository, result.CurrentVersionSource);
        Assert.Null(result.CurrentVersion);
    }

    [Fact]
    public void Step_LocalPsd1VersioningDoesNotQueryPowerShellGallery()
    {
        using var directory = new TemporaryDirectory();
        var manifestPath = Path.Combine(directory.Path, "OfflineModule.psd1");
        File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.2.3' }");

        var handler = new ThrowingPowerShellGalleryHandler("Gallery socket unavailable.");
        using var client = new HttpClient(handler);
        var stepper = new ModuleVersionStepper(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(1, string.Empty, "Find-PSResource socket unavailable.", "pwsh.exe")),
            client);

        var result = stepper.Step("1.2.X", moduleName: "OfflineModule", localPsd1Path: manifestPath, repository: "PSGallery");

        Assert.Equal("1.2.4", result.Version);
        Assert.Equal(ModuleVersionSource.LocalPsd1, result.CurrentVersionSource);
        Assert.Equal("1.2.3", result.CurrentVersion);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public void Step_ReportsBothGalleryFailuresWithoutScanningFromBaseVersion()
    {
        var handler = new ThrowingPowerShellGalleryHandler("Gallery socket unavailable.");
        using var client = new HttpClient(handler);
        var stepper = new ModuleVersionStepper(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(1, string.Empty, "Find-PSResource socket unavailable.", "pwsh.exe")),
            client);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            stepper.Step("3.0.X", moduleName: "PSPublishModule", localPsd1Path: null, repository: "PSGallery"));

        Assert.Contains("both PowerShell Gallery lookup paths failed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No candidate version was selected", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Gallery socket unavailable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Find-PSResource", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public void Step_RejectsCandidateWhenAvailabilityProbeFails()
    {
        using var client = new HttpClient(new AvailabilityFailureHandler());
        var stepper = new ModuleVersionStepper(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "3.0.55"), string.Empty, "pwsh.exe")),
            client);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            stepper.Step("3.0.X", moduleName: "PSPublishModule", localPsd1Path: null, repository: "PSGallery"));

        Assert.Contains("Unable to verify whether PowerShell Gallery version '3.0.56' is available", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No version was selected to avoid reusing an existing package version", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Exact-version socket unavailable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Step_RejectsSuccessfulExactLookupWithMismatchedVersionMetadata()
    {
        using var client = new HttpClient(new MismatchedVersionHandler());
        var stepper = new ModuleVersionStepper(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "3.0.55"), string.Empty, "pwsh.exe")),
            client);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            stepper.Step("3.0.X", moduleName: "PSPublishModule", localPsd1Path: null, repository: "PSGallery"));

        Assert.Contains("Unable to verify whether PowerShell Gallery version '3.0.56' is available", exception.Message, StringComparison.Ordinal);
        Assert.Contains("contained version '9.9.9'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPatternStepper_RejectsPatternWhoseFixedPrefixIsBelowCurrentVersion()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            VersionPatternStepper.Step("1.2.X", new Version(2, 0, 0)));

        Assert.Contains("cannot produce a version greater than current version '2.0.0'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fixed prefix is lower", exception.Message, StringComparison.Ordinal);
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
            if (!uri.Contains("FindPackagesById", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

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

    private sealed class FakeReservedPowerShellGalleryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;

            if (uri.Contains("FindPackagesById", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
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
                        </feed>
                        """, Encoding.UTF8, "application/atom+xml")
                });
            }

            if (uri.Contains("Packages(", StringComparison.OrdinalIgnoreCase) &&
                uri.Contains("PSPublishModule", StringComparison.OrdinalIgnoreCase) &&
                uri.Contains("3.0.0", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        <?xml version="1.0" encoding="utf-8"?>
                        <entry xml:base="https://www.powershellgallery.com/api/v2"
                               xmlns="http://www.w3.org/2005/Atom"
                               xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                               xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                          <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/PSPublishModule/3.0.0" />
                          <m:properties>
                            <d:Version>3.0.0</d:Version>
                            <d:IsPrerelease>false</d:IsPrerelease>
                            <d:Published m:type="Edm.DateTime">1900-01-01T00:00:00</d:Published>
                          </m:properties>
                        </entry>
                        """, Encoding.UTF8, "application/atom+xml")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FakeCandidateReservationHandler : HttpMessageHandler
    {
        private readonly HashSet<string> _reservedVersions;

        public FakeCandidateReservationHandler(params string[] reservedVersions)
        {
            _reservedVersions = new HashSet<string>(reservedVersions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;

            if (uri.Contains("FindPackagesById", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        <?xml version="1.0" encoding="utf-8"?>
                        <feed xmlns="http://www.w3.org/2005/Atom"
                              xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                              xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                        </feed>
                        """, Encoding.UTF8, "application/atom+xml")
                });
            }

            if (uri.Contains("Packages(", StringComparison.OrdinalIgnoreCase) &&
                uri.Contains("PSPublishModule", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var version in _reservedVersions)
                {
                    if (uri.Contains(version, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent($$"""
                                <?xml version="1.0" encoding="utf-8"?>
                                <entry xml:base="https://www.powershellgallery.com/api/v2"
                                       xmlns="http://www.w3.org/2005/Atom"
                                       xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                                       xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                                  <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/PSPublishModule/{{version}}" />
                                  <m:properties>
                                    <d:Version>{{version}}</d:Version>
                                    <d:IsPrerelease>false</d:IsPrerelease>
                                    <d:Published m:type="Edm.DateTime">1900-01-01T00:00:00</d:Published>
                                  </m:properties>
                                </entry>
                                """, Encoding.UTF8, "application/atom+xml")
                        });
                    }
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class ThrowingPowerShellGalleryHandler : HttpMessageHandler
    {
        private readonly string _message;

        public ThrowingPowerShellGalleryHandler(string message)
        {
            _message = message;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new HttpRequestException(_message);
        }
    }

    private sealed class AvailabilityFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("FindPackagesById", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildVersionFeed("3.0.55"), Encoding.UTF8, "application/atom+xml")
                });
            }

            throw new HttpRequestException("Exact-version socket unavailable.");
        }
    }

    private sealed class MismatchedVersionHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            var body = uri.Contains("FindPackagesById", StringComparison.OrdinalIgnoreCase)
                ? BuildVersionFeed("3.0.55")
                : BuildVersionEntry("9.9.9");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/atom+xml")
            });
        }
    }

    private static string BuildVersionFeed(string version)
        => $$"""
           <?xml version="1.0" encoding="utf-8"?>
           <feed xmlns="http://www.w3.org/2005/Atom"
                 xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                 xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
             <entry>
               <m:properties>
                 <d:Version>{{version}}</d:Version>
                 <d:IsPrerelease>false</d:IsPrerelease>
                 <d:Published m:type="Edm.DateTime">2026-03-10T10:00:00</d:Published>
               </m:properties>
             </entry>
           </feed>
           """;

    private static string BuildVersionEntry(string version)
        => $$"""
           <?xml version="1.0" encoding="utf-8"?>
           <entry xmlns="http://www.w3.org/2005/Atom"
                  xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                  xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
             <m:properties>
               <d:Version>{{version}}</d:Version>
             </m:properties>
           </entry>
           """;
}
