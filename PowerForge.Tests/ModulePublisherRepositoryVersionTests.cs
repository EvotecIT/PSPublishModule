using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePublisherRepositoryVersionTests
{
    [Fact]
    public void PSResourceGetClient_Find_PreservesPrereleaseField()
    {
        var stdout = string.Join("::", new[]
        {
            "PFPSRG::ITEM",
            Encode("Mailozaurr"),
            Encode("2.0.1"),
            Encode("PSGallery"),
            Encode("Przemyslaw Klys"),
            Encode("Mailozaurr"),
            Encode("2b0ea9f1-3ff1-4300-b939-106d5da608fa"),
            Encode("Preview1")
        });

        var client = new PSResourceGetClient(
            new StubPowerShellRunner(new PowerShellRunResult(0, stdout, string.Empty, "pwsh.exe")),
            new NullLogger());

        var results = client.Find(
            new PSResourceFindOptions(
                names: new[] { "Mailozaurr" },
                prerelease: true,
                repositories: new[] { "PSGallery" }));

        var item = Assert.Single(results);
        Assert.Equal("2.0.1", item.Version);
        Assert.Equal("Preview1", item.PreRelease);
    }

    [Fact]
    public void GetRepositoryVersionText_AppendsPrereleaseSuffix()
    {
        var resource = new PSResourceInfo(
            name: "Mailozaurr",
            version: "2.0.1",
            repository: "PSGallery",
            author: null,
            description: null,
            guid: null,
            preRelease: "Preview1");

        var versionText = ModulePublisher.GetRepositoryVersionText(resource);

        Assert.Equal("2.0.1-Preview1", versionText);
    }

    [Fact]
    public void GetGitHubTag_Default_IncludesPrereleaseSuffix()
    {
        var publish = new PublishConfiguration
        {
            Destination = PublishDestination.GitHub,
            Enabled = true
        };

        var tag = ModulePublisher.GetGitHubTag(
            publish,
            moduleName: "Mailozaurr",
            resolvedVersion: "2.0.1",
            preRelease: "Preview2");

        Assert.Equal("v2.0.1-Preview2", tag);
    }

    [Fact]
    public void EnsureVersionIsGreaterThanRepository_UsesUnlistedPowerShellGalleryVersions()
    {
        using var client = new HttpClient(new FakePowerShellGalleryFeedHandler());
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "2.0.27"), string.Empty, "pwsh.exe")),
            client);

        var exception = Assert.Throws<InvalidOperationException>(() => publisher.EnsureVersionIsGreaterThanRepository(
            PublishTool.PSResourceGet,
            moduleName: "PSPublishModule",
            moduleVersion: "3.0.0",
            preRelease: null,
            repositoryName: "PSGallery",
            credential: null));

        Assert.Contains("not greater than repository version '3.0.0'", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureVersionIsGreaterThanRepository_TreatsMissingRepositoryPackageAsFirstPublish()
    {
        var error = "Find-PSResource failed (exit 1). Package with name 'EntraIDConfig' could not be found in repository 'CompanyGallery'.";
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(1, string.Empty, error, "pwsh.exe")));

        var exception = Record.Exception(() => publisher.EnsureVersionIsGreaterThanRepository(
            PublishTool.PSResourceGet,
            moduleName: "EntraIDConfig",
            moduleVersion: "2.4.0",
            preRelease: null,
            repositoryName: "CompanyGallery",
            credential: null));

        Assert.Null(exception);
    }

    [Fact]
    public void IsRepositoryPackageNotFound_DoesNotHideRepositoryFailuresForOtherPackages()
    {
        var exception = new InvalidOperationException("Package with name 'OtherModule' could not be found in repository 'CompanyGallery'.");

        Assert.False(ModulePublisher.IsRepositoryPackageNotFound("EntraIDConfig", exception));
    }

    [Fact]
    public void Publish_RegistersConfiguredRepositoryBeforeVersionCheck()
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        var calls = new List<string>();
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var manifestPath = Path.Combine(stagingRoot, "PSPublishModule.psd1");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '3.0.13'; GUID = 'eb76426a-1992-40a5-82cd-6480f883ef4d'; RootModule = 'PSPublishModule.psm1' }");
            File.WriteAllText(Path.Combine(stagingRoot, "PSPublishModule.psm1"), string.Empty);

            var runner = new StubPowerShellRunner(request =>
            {
                var script = File.ReadAllText(request.ScriptPath!);
                if (script.Contains("Register-PSResourceRepository", StringComparison.Ordinal))
                {
                    calls.Add("register");
                    return new PowerShellRunResult(0, "PFPSRG::REPO::CREATED::0", string.Empty, "pwsh.exe");
                }

                if (script.Contains("Find-PSResource", StringComparison.Ordinal))
                {
                    calls.Add("find");
                    return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe");
                }

                if (script.Contains("Publish-PSResource", StringComparison.Ordinal))
                {
                    calls.Add("publish");
                    return new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", string.Empty, "pwsh.exe");
                }

                throw new InvalidOperationException("Unexpected PowerShell script invocation.");
            });
            var publisher = new ModulePublisher(new NullLogger(), runner);

            var publish = new PublishConfiguration
            {
                Destination = PublishDestination.PowerShellGallery,
                Enabled = true,
                Tool = PublishTool.PSResourceGet,
                ApiKey = "AzureDevOps",
                RepositoryName = "EvotecPowerShellGallery",
                Repository = new PublishRepositoryConfiguration
                {
                    Name = "EvotecPowerShellGallery",
                    Uri = "https://pkgs.dev.azure.com/evotecpl/PowerShellGallery/_packaging/PowerShellGalleryFeed/nuget/v3/index.json",
                    Trusted = true,
                    ApiVersion = RepositoryApiVersion.V3,
                    EnsureRegistered = true
                }
            };
            var plan = CreatePlan();
            var buildResult = new ModuleBuildResult(
                stagingPath: stagingRoot,
                manifestPath: manifestPath,
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var result = publisher.Publish(publish, plan, buildResult, Array.Empty<ArtefactBuildResult>());

            Assert.True(result.Succeeded);
            Assert.Equal(new[] { "register", "find", "publish" }, calls);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

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

    private static ModulePipelinePlan CreatePlan()
    {
        return new ModulePipelinePlan(
            moduleName: "PSPublishModule",
            projectRoot: @"C:\repo\PSPublishModule",
            expectedVersion: "3.0.13",
            resolvedVersion: "3.0.13",
            preRelease: null,
            manifest: null,
            buildSpec: new ModuleBuildSpec
            {
                Name = "PSPublishModule",
                SourcePath = @"C:\repo\PSPublishModule",
                Version = "3.0.13"
            },
            resolvedCsprojPath: null,
            syncNETProjectVersion: false,
            compatiblePSEditions: Array.Empty<string>(),
            requiredModules: Array.Empty<RequiredModuleReference>(),
            externalModuleDependencies: Array.Empty<string>(),
            requiredModulesForPackaging: Array.Empty<RequiredModuleReference>(),
            information: null,
            documentation: null,
            delivery: null,
            documentationBuild: null,
            compatibilitySettings: null,
            fileConsistencySettings: null,
            validationSettings: null,
            formatting: null,
            importModules: null,
            placeHolders: Array.Empty<PlaceHolderReplacement>(),
            placeHolderOption: null,
            commandModuleDependencies: new Dictionary<string, string[]>(),
            testsAfterMerge: Array.Empty<TestConfiguration>(),
            mergeModule: false,
            mergeMissing: false,
            doNotAttemptToFixRelativePaths: false,
            approvedModules: Array.Empty<string>(),
            moduleSkip: null,
            signModule: false,
            signing: null,
            publishes: Array.Empty<ConfigurationPublishSegment>(),
            artefacts: Array.Empty<ConfigurationArtefactSegment>(),
            installEnabled: false,
            installStrategy: InstallationStrategy.AutoRevision,
            installKeepVersions: 3,
            installRoots: Array.Empty<string>(),
            installLegacyFlatHandling: LegacyFlatModuleHandling.Warn,
            installPreserveVersions: Array.Empty<string>(),
            installMissingModules: false,
            installMissingModulesForce: false,
            installMissingModulesPrerelease: false,
            installMissingModulesRepository: null,
            installMissingModulesCredential: null,
            stagingWasGenerated: true,
            deleteGeneratedStagingAfterRun: true);
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _run;

        public StubPowerShellRunner(PowerShellRunResult result)
        {
            _run = _ => result;
        }

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> run)
        {
            _run = run;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            return _run(request);
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
