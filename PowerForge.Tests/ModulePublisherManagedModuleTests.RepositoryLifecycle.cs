using System.Text;

namespace PowerForge.Tests;

public sealed partial class ModulePublisherManagedModuleTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Split_feed_publish_preserves_consumer_alias_and_cleans_only_owned_upload_alias(
        bool sharedPublisher, bool existingUpload, bool uploadFails)
    {
        using var root = new TemporaryDirectory();
        using var readFeed = new TemporaryDirectory();
        using var upload = new TemporaryDirectory();
        var manifest = WriteDependencyModule(root.Path);
        var publish = DependencyPublish(readFeed.Path, upload.Path, true);
        publish.Tool = PublishTool.PSResourceGet;
        publish.ApiKey = "push-key";
        publish.Force = true;
        var registrations = new Dictionary<string, string> { ["Local"] = readFeed.Path };
        if (existingUpload)
            registrations["Upload"] = upload.Path;
        string? acquiredName = null;
        var cleaned = false;
        var runner = new StubPowerShellRunner(request =>
        {
            var script = File.ReadAllText(request.ScriptPath!);
            if (script.Contains("PFPSRG::REPO::CREATED", StringComparison.Ordinal))
            {
                Assert.Equal("1", request.Arguments[5]);
                Assert.NotEqual("Local", request.Arguments[0]);
                Assert.Equal(upload.Path, request.Arguments[1]);
                acquiredName = existingUpload ? "Upload" : request.Arguments[0];
                if (!existingUpload)
                    registrations.Add(acquiredName, upload.Path);
                return new PowerShellRunResult(0,
                    "PFPSRG::REPO::NAME::" + Convert.ToBase64String(Encoding.UTF8.GetBytes(acquiredName)) +
                    "\nPFPSRG::REPO::CREATED::" + (existingUpload ? "0" : "1"), "", "pwsh");
            }
            if (script.Contains("Unregister-PSResourceRepository", StringComparison.Ordinal))
            {
                Assert.False(existingUpload);
                Assert.Equal(acquiredName, request.Arguments[0]);
                registrations.Remove(request.Arguments[0]);
                cleaned = true;
                return new PowerShellRunResult(0, "", "", "pwsh");
            }
            Assert.Contains("PFPSRG::PUBLISH::OK", script);
            Assert.Equal(acquiredName, request.Arguments[2]);
            if (uploadFails)
                throw new InvalidOperationException("Synthetic upload failure");
            return new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", "", "pwsh");
        });
        void Publish()
        {
            if (sharedPublisher)
            {
                var result = new RepositoryPublisher(new NullLogger(), runner).Publish(new RepositoryPublishRequest
                {
                    Path = root.Path, Tool = PublishTool.PSResourceGet, ApiKey = publish.ApiKey,
                    Repository = publish.Repository, SkipDependenciesCheck = true
                });
                Assert.Equal("Local", result.RepositoryName);
            }
            else
            {
                var result = new ModulePublisher(new NullLogger(), runner).Publish(publish, CreatePlan(root.Path),
                    new ModuleBuildResult(root.Path, manifest, new ExportSet([], [], [])), []);
                Assert.True(result.Succeeded);
            }
        }
        if (uploadFails)
            Assert.Contains("Synthetic upload failure", Assert.Throws<InvalidOperationException>(Publish).Message);
        else
            Publish();
        Assert.NotNull(acquiredName);
        Assert.Equal(readFeed.Path, registrations["Local"]);
        Assert.Equal(existingUpload ? 2 : 1, registrations.Count);
        Assert.Equal(!existingUpload, cleaned);
    }

    [Theory]
    [InlineData(PublishTool.Auto, true, true)]
    [InlineData(PublishTool.ManagedModule, true, false)]
    [InlineData(PublishTool.PSResourceGet, true, false)]
    [InlineData(PublishTool.PowerShellGet, false, false)]
    [InlineData(PublishTool.PowerShellGet, false, true)]
    public void Version_preflight_rejects_invalid_dependency_policy_before_repository_operations(
        PublishTool tool, bool mirror, bool force)
    {
        using var root = new TemporaryDirectory();
        var publish = DependencyPublish(root.Path, root.Path, true);
        publish.Tool = tool;
        publish.PublishRequiredModules = mirror;
        publish.Force = force;
        var publisher = new ModulePublisher(new NullLogger(), new StubPowerShellRunner(_ =>
            throw new Exception("Repository operation occurred before validation")));
        var error = Assert.ThrowsAny<Exception>(() => publisher.ValidateVersionForPublish(publish, CreatePlan(root.Path)));
        Assert.Contains("SkipDependenciesCheck", error.Message);
    }

    [Theory]
    [InlineData(PrivateGalleryProvider.JFrog)]
    [InlineData(PrivateGalleryProvider.AzureArtifacts)]
    public void Managed_reads_preserve_provider_preset_v3_endpoint(PrivateGalleryProvider provider)
    {
        var endpoints = PrivateGalleryRepositoryEndpoints.Create(provider,
            azureDevOpsOrganization: "company", azureDevOpsProject: "project", azureArtifactsFeed: "feed",
            repositoryName: "Company", jfrogBaseUri: "https://company.jfrog.io/artifactory", jfrogRepository: "feed");
        var repository = new PublishRepositoryConfiguration
        {
            Uri = endpoints.PSResourceGetUri,
            SourceUri = endpoints.PowerShellGetSourceUri,
            PublishUri = endpoints.PowerShellGetPublishUri,
            UseProviderEndpointDefaults = true
        };
        Assert.Equal(endpoints.PSResourceGetUri, ModulePublishDependencyPolicy.ReadUri(repository));
        Assert.False(ModulePublishDependencyPolicy.HasSeparateEndpoints(repository));
        Assert.Equal(endpoints.PSResourceGetUri, ModulePublishDependencyPolicy.PSResourceGetRegistrationUri(repository));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preset_protocol_defaults_survive_deferred_secret_and_path_copies(bool azureArtifacts)
    {
        using var root = new TemporaryDirectory();
        var manifest = WriteDependencyModule(root.Path);
        File.WriteAllText(Path.Combine(root.Path, "publish.key"), "synthetic-key");
        var configuration = new PublishConfigurationFactory().Create(new PublishConfigurationRequest
        {
            ParameterSetName = azureArtifacts ? "AzureArtifacts" : "JFrog",
            AzureDevOpsOrganization = "company", AzureArtifactsFeed = "feed",
            JFrogBaseUri = azureArtifacts ? null : "https://company.jfrog.io/artifactory",
            JFrogRepository = azureArtifacts ? null : "feed",
            Tool = PublishTool.PSResourceGet, SkipDependenciesCheck = true
        }).Configuration;
        configuration.Enabled = true;
        configuration.Force = true;
        configuration.ApiKey = "";
        configuration.ApiKeyFilePath = "publish.key";
        var registrationSeen = false;
        var runner = new StubPowerShellRunner(request =>
        {
            var script = File.ReadAllText(request.ScriptPath!);
            if (script.Contains("PFPSRG::REPO::CREATED", StringComparison.Ordinal))
            {
                Assert.Equal(configuration.Repository!.Uri, request.Arguments[1]);
                Assert.Equal("0", request.Arguments[5]);
                registrationSeen = true;
                return new PowerShellRunResult(0, "PFPSRG::REPO::CREATED::0", "", "pwsh");
            }
            Assert.Contains("PFPSRG::PUBLISH::OK", script);
            return new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", "", "pwsh");
        });
        var result = new ModulePublisher(new NullLogger(), runner).Publish(configuration, CreatePlan(root.Path),
            new ModuleBuildResult(root.Path, manifest, new ExportSet([], [], [])), []);
        Assert.True(result.Succeeded);
        Assert.True(registrationSeen);
    }
}
