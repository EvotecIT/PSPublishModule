using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePublisherRepositoryVersionTests
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
    public void ResolvePublishApiKey_ResolvesDeferredFilePathAgainstProjectRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        var other = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        try
        {
            var secretDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, ".secrets"));
            File.WriteAllText(Path.Combine(secretDirectory.FullName, "gallery.key"), " project-token ");
            Directory.SetCurrentDirectory(other.FullName);

            var apiKey = ModulePublisher.ResolvePublishApiKey(
                new PublishConfiguration
                {
                    ApiKeyFilePath = Path.Combine(".secrets", "gallery.key")
                },
                root.FullName);

            Assert.Equal("project-token", apiKey);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            try { root.Delete(recursive: true); } catch { }
            try { other.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolvePublishApiKey_RejectsDeferredMultilineSecretAtPublishRuntime()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        try
        {
            var path = Path.Combine(root.FullName, "gallery.key");
            File.WriteAllText(path, "first-line" + Environment.NewLine + "second-line");

            var ex = Assert.Throws<ArgumentException>(() => ModulePublisher.ResolvePublishApiKey(
                new PublishConfiguration { ApiKeyFilePath = path },
                root.FullName));

            Assert.Contains("multi-line secret", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("single line", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not a script", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
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
    public void ValidateVersionForPublish_RejectsSynchronizedVersionAlreadyInRepository()
    {
        using var client = new HttpClient(new FakePowerShellGalleryFeedHandler());
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "3.0.0"), string.Empty, "pwsh.exe")),
            client);
        var publish = new PublishConfiguration
        {
            Enabled = true,
            Destination = PublishDestination.PowerShellGallery,
            RepositoryName = "PSGallery",
            Tool = PublishTool.PSResourceGet
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            publisher.ValidateVersionForPublish(publish, CreatePlan(resolvedVersion: "3.0.0")));

        Assert.Contains("not greater than repository version '3.0.0'", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateVersionForPublish_AllowsExactRepositoryVersionWhenResumingRelease()
    {
        using var client = new HttpClient(new FakePowerShellGalleryFeedHandler());
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "3.0.0"), string.Empty, "pwsh.exe")),
            client);
        var publish = new PublishConfiguration
        {
            Enabled = true,
            Destination = PublishDestination.PowerShellGallery,
            RepositoryName = "PSGallery",
            Tool = PublishTool.PSResourceGet
        };

        var result = publisher.ValidateVersionForPublish(
            publish,
            CreatePlan(resolvedVersion: "3.0.0"),
            allowExistingExactVersion: true);

        Assert.Equal(ModulePublishVersionPreflightResult.AlreadyPublished, result);
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
    public void IsRepositoryPackageNotFound_TreatsManagedVersionQueryNotFoundAsFirstPublish()
    {
        var exception = new ManagedModuleRepositoryException(
            "VersionQuery",
            "CompanyGallery",
            "https://gallery.example.test/index.json",
            "Unable to query versions for package 'EntraIDConfig'.",
            "Check repository availability.",
            statusCode: 404);

        Assert.True(ModulePublisher.IsRepositoryPackageNotFound("EntraIDConfig", exception));
    }

    [Fact]
    public void IsRepositoryPackageNotFound_TreatsMissingLocalManagedFeedAsFirstPublish()
    {
        var exception = new ManagedModuleRepositoryException(
            "VersionQuery",
            "Local",
            Path.Combine(Path.GetTempPath(), "missing-feed"),
            "Local repository folder was not found: C:\\missing-feed",
            "Create the feed.");

        Assert.True(ModulePublisher.IsRepositoryPackageNotFound("EntraIDConfig", exception));
    }

    [Fact]
    public void IsRepositoryPackageNotFound_TreatsMissingRelativeLocalManagedFeedAsFirstPublish()
    {
        var exception = new ManagedModuleRepositoryException(
            "VersionQuery",
            "Local",
            @".\feed",
            @"Local repository folder was not found: .\feed",
            "Create the feed.");

        Assert.True(ModulePublisher.IsRepositoryPackageNotFound("EntraIDConfig", exception));
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

    [Fact]
    public void Publish_AllowsRuntimeCredentialProviderWithoutApiKeyOrStaticCredential()
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        var toolRoot = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var calls = new List<string>();
        try
        {
            Directory.CreateDirectory(stagingRoot);
            Directory.CreateDirectory(toolRoot);
            File.WriteAllText(Path.Combine(toolRoot, Path.DirectorySeparatorChar == '\\' ? "jf.exe" : "jf"), string.Empty);
            Environment.SetEnvironmentVariable(
                "PATH",
                string.IsNullOrWhiteSpace(originalPath)
                    ? toolRoot
                    : toolRoot + Path.PathSeparator + originalPath);

            var manifestPath = Path.Combine(stagingRoot, "PSPublishModule.psd1");
            File.WriteAllText(
                manifestPath,
                """
                @{
                    ModuleVersion = '3.0.13'
                    GUID = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
                    RootModule = 'PSPublishModule.psm1'
                    RequiredModules = @(
                        @{ ModuleName = 'DependencyModule'; ModuleVersion = '1.0.0' }
                    )
                }
                """);
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
                    Assert.Equal("oidc-user@example.com", request.Arguments[4]);
                    Assert.Equal("jfrog-access-token", request.Arguments[5]);

                    var names = DecodeLines(request.Arguments[0]);
                    if (names.Contains("PSPublishModule", StringComparer.OrdinalIgnoreCase))
                        return new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "3.0.12"), string.Empty, "pwsh.exe");
                    if (names.Contains("DependencyModule", StringComparer.OrdinalIgnoreCase))
                        return new PowerShellRunResult(0, VisibleRepositoryItem("DependencyModule", "1.2.3"), string.Empty, "pwsh.exe");

                    return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe");
                }

                if (script.Contains("Publish-PSResource", StringComparison.Ordinal))
                {
                    calls.Add("publish");
                    Assert.Equal("oidc-user@example.com", request.Arguments[7]);
                    Assert.Equal("jfrog-access-token", request.Arguments[8]);
                    return new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", string.Empty, "pwsh.exe");
                }

                throw new InvalidOperationException("Unexpected PowerShell script invocation.");
            });
            var processRunner = new StubProcessRunner(_ => new ProcessRunResult(
                0,
                """{"access_token":"jfrog-access-token","username":"oidc-user@example.com"}""",
                string.Empty,
                "jf.exe",
                TimeSpan.FromMilliseconds(10),
                timedOut: false));
            var publisher = new ModulePublisher(new NullLogger(), runner, client: null, processRunner: processRunner);

            var publish = new PublishConfiguration
            {
                Destination = PublishDestination.PowerShellGallery,
                Enabled = true,
                Tool = PublishTool.PSResourceGet,
                RepositoryName = "JFrogPS",
                Repository = new PublishRepositoryConfiguration
                {
                    Name = "JFrogPS",
                    Uri = "https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json",
                    EnsureRegistered = true,
                    CredentialProvider = new RepositoryCredentialProviderConfiguration
                    {
                        Kind = RepositoryCredentialProviderKind.JFrogOidc,
                        JFrogPlatformUri = "https://company.jfrog.io/",
                        JFrogOidcProvider = "azure-oidc",
                        JFrogOidcProviderType = JFrogOidcProviderType.Azure,
                        JFrogOidcTokenId = "ci-jwt"
                    }
                }
            };
            var plan = CreatePlan();
            var buildResult = new ModuleBuildResult(
                stagingPath: stagingRoot,
                manifestPath: manifestPath,
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var result = publisher.Publish(publish, plan, buildResult, Array.Empty<ArtefactBuildResult>());

            Assert.True(result.Succeeded);
            Assert.Equal(new[] { "register", "find", "find", "publish" }, calls);
            var processRequest = Assert.Single(processRunner.Requests);
            Assert.Equal(new[] { "eot", "azure-oidc", "--url=https://company.jfrog.io/", "--oidc-provider-type=Azure" }, processRequest.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
            if (Directory.Exists(toolRoot))
                Directory.Delete(toolRoot, recursive: true);
        }
    }

    [Fact]
    public void Publish_PublishesMissingRequiredModulesWhenEnabled()
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        var calls = new List<string>();
        var dependencyPublished = false;
        var otherDependencyPublished = false;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var manifestPath = Path.Combine(stagingRoot, "PSPublishModule.psd1");
            File.WriteAllText(
                manifestPath,
                """
                @{
                    ModuleVersion = '3.0.13'
                    GUID = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
                    RootModule = 'PSPublishModule.psm1'
                    RequiredModules = @(
                        @{ ModuleName = 'DependencyModule'; ModuleVersion = '1.0.0'; MaximumVersion = '1.5.0' },
                        @{ ModuleName = 'OtherDependency'; ModuleVersion = '1.0.0'; MaximumVersion = '1.5.0' }
                    )
                }
                """);
            File.WriteAllText(Path.Combine(stagingRoot, "PSPublishModule.psm1"), string.Empty);

            var runner = new StubPowerShellRunner(request =>
            {
                var script = File.ReadAllText(request.ScriptPath!);
                if (script.Contains("Find-PSResource", StringComparison.Ordinal))
                {
                    var names = DecodeLines(request.Arguments[0]);
                    var repositories = DecodeLines(request.Arguments[2]);

                    if (names.Contains("PSPublishModule", StringComparer.OrdinalIgnoreCase))
                    {
                        calls.Add("find-main");
                        Assert.Equal("CompanyGallery", Assert.Single(repositories));
                        Assert.Equal("publisher", request.Arguments[4]);
                        Assert.Equal("target-token", request.Arguments[5]);
                        return new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "3.0.12"), string.Empty, "pwsh.exe");
                    }

                    if (names.Contains("DependencyModule", StringComparer.OrdinalIgnoreCase) &&
                        repositories.Contains("CompanyGallery", StringComparer.OrdinalIgnoreCase))
                    {
                        Assert.Equal("0", request.Arguments[3]);
                        Assert.Equal("publisher", request.Arguments[4]);
                        Assert.Equal("target-token", request.Arguments[5]);
                        if (string.Equals(request.Arguments[1], "[1.2.3]", StringComparison.Ordinal))
                        {
                            calls.Add("find-target-dependency-package");
                            Assert.Equal("[1.2.3]", request.Arguments[1]);
                            if (!dependencyPublished)
                            {
                                return new PowerShellRunResult(
                                    1,
                                    string.Empty,
                                    "Package with name 'DependencyModule' could not be found in repository 'CompanyGallery'.",
                                    "pwsh.exe");
                            }

                            return new PowerShellRunResult(
                                0,
                                VisibleRepositoryItem("DependencyModule", "1.2.3"),
                                string.Empty,
                                "pwsh.exe");
                        }

                        calls.Add("find-target-dependency");
                        Assert.Equal("[1.0.0,1.5.0]", request.Arguments[1]);
                        if (!dependencyPublished)
                        {
                            return new PowerShellRunResult(
                                0,
                                VisibleRepositoryItem("DependencyModule", "2.0.0", "preview1"),
                                string.Empty,
                                "pwsh.exe");
                        }

                        return new PowerShellRunResult(
                            0,
                            VisibleRepositoryItem("DependencyModule", "1.2.3"),
                            string.Empty,
                            "pwsh.exe");
                    }

                    if (names.Contains("OtherDependency", StringComparer.OrdinalIgnoreCase) &&
                        repositories.Contains("CompanyGallery", StringComparer.OrdinalIgnoreCase))
                    {
                        Assert.Equal("0", request.Arguments[3]);
                        Assert.Equal("publisher", request.Arguments[4]);
                        Assert.Equal("target-token", request.Arguments[5]);
                        if (string.Equals(request.Arguments[1], "[1.2.3]", StringComparison.Ordinal))
                        {
                            calls.Add("find-target-other-dependency-package");
                            Assert.Equal("[1.2.3]", request.Arguments[1]);
                            if (!otherDependencyPublished)
                            {
                                return new PowerShellRunResult(
                                    1,
                                    string.Empty,
                                    "Package with name 'OtherDependency' could not be found in repository 'CompanyGallery'.",
                                    "pwsh.exe");
                            }

                            return new PowerShellRunResult(
                                0,
                                VisibleRepositoryItem("OtherDependency", "1.2.3"),
                                string.Empty,
                                "pwsh.exe");
                        }

                        calls.Add("find-target-other-dependency");
                        Assert.Equal("[1.0.0,1.5.0]", request.Arguments[1]);
                        if (otherDependencyPublished)
                        {
                            return new PowerShellRunResult(
                                0,
                                VisibleRepositoryItem("OtherDependency", "1.2.3"),
                                string.Empty,
                                "pwsh.exe");
                        }

                        return new PowerShellRunResult(
                            1,
                            string.Empty,
                            "Package with name 'OtherDependency' could not be found in repository 'CompanyGallery'.",
                            "pwsh.exe");
                    }

                    if (names.Contains("DependencySupport", StringComparer.OrdinalIgnoreCase) &&
                        repositories.Contains("CompanyGallery", StringComparer.OrdinalIgnoreCase))
                    {
                        calls.Add("find-target-transitive-dependency");
                        Assert.Equal("[1.0.0]", request.Arguments[1]);
                        Assert.Equal("0", request.Arguments[3]);
                        Assert.Equal("publisher", request.Arguments[4]);
                        Assert.Equal("target-token", request.Arguments[5]);
                        return new PowerShellRunResult(
                            0,
                            VisibleRepositoryItem("DependencySupport", "1.0.0"),
                            string.Empty,
                            "pwsh.exe");
                    }

                    if (names.Contains("DependencyModule", StringComparer.OrdinalIgnoreCase) &&
                        repositories.Contains("InternalUpstream", StringComparer.OrdinalIgnoreCase))
                    {
                        calls.Add("find-source-dependency");
                        Assert.Equal("[1.0.0,1.5.0]", request.Arguments[1]);
                        Assert.Equal("0", request.Arguments[3]);
                        Assert.Equal(string.Empty, request.Arguments[4]);
                        Assert.Equal(string.Empty, request.Arguments[5]);
                        return new PowerShellRunResult(
                            0,
                            string.Join(Environment.NewLine, new[]
                            {
                                VisibleRepositoryItem("DependencyModule", "1.2.3"),
                                VisibleRepositoryItem("DependencyModule", "1.6.0")
                            }),
                            string.Empty,
                            "pwsh.exe");
                    }

                    if (names.Contains("OtherDependency", StringComparer.OrdinalIgnoreCase) &&
                        repositories.Contains("InternalUpstream", StringComparer.OrdinalIgnoreCase))
                    {
                        calls.Add("find-source-other-dependency");
                        Assert.Equal("[1.0.0,1.5.0]", request.Arguments[1]);
                        Assert.Equal("0", request.Arguments[3]);
                        Assert.Equal(string.Empty, request.Arguments[4]);
                        Assert.Equal(string.Empty, request.Arguments[5]);
                        return new PowerShellRunResult(
                            0,
                            string.Join(Environment.NewLine, new[]
                            {
                                VisibleRepositoryItem("OtherDependency", "1.2.3"),
                                VisibleRepositoryItem("OtherDependency", "1.6.0")
                            }),
                            string.Empty,
                            "pwsh.exe");
                    }

                    return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe");
                }

                if (script.Contains("Save-PSResource", StringComparison.Ordinal))
                {
                    var savedName = request.Arguments[0];
                    calls.Add(savedName.Equals("DependencyModule", StringComparison.OrdinalIgnoreCase)
                        ? "save-dependency"
                        : "save-other-dependency");
                    Assert.Contains(savedName, new[] { "DependencyModule", "OtherDependency" });
                    Assert.Equal("1.2.3", request.Arguments[1]);
                    Assert.Equal("InternalUpstream", request.Arguments[2]);
                    Assert.Equal("0", request.Arguments[6]);
                    Assert.Equal(string.Empty, request.Arguments[9]);
                    Assert.Equal(string.Empty, request.Arguments[10]);

                    var transitiveModulePath = Path.Combine(request.Arguments[3], "DependencySupport", "1.0.0");
                    Directory.CreateDirectory(transitiveModulePath);
                    File.WriteAllText(Path.Combine(transitiveModulePath, "DependencySupport.psd1"), "@{ ModuleVersion = '1.0.0'; RootModule = 'DependencySupport.psm1' }");
                    File.WriteAllText(Path.Combine(transitiveModulePath, "DependencySupport.psm1"), string.Empty);

                    var savedModulePath = Path.Combine(request.Arguments[3], savedName, "1.2.3");
                    Directory.CreateDirectory(savedModulePath);
                    File.WriteAllText(Path.Combine(savedModulePath, $"{savedName}.psd1"), $"@{{ ModuleVersion = '1.2.3'; RootModule = '{savedName}.psm1' }}");
                    File.WriteAllText(Path.Combine(savedModulePath, $"{savedName}.psm1"), string.Empty);

                    return new PowerShellRunResult(
                        0,
                        string.Join(Environment.NewLine, new[]
                        {
                            SaveRepositoryItem(savedName, "1.2.3"),
                            SaveRepositoryItem("DependencySupport", "1.0.0")
                        }),
                        string.Empty,
                        "pwsh.exe");
                }

                if (script.Contains("Publish-PSResource", StringComparison.Ordinal))
                {
                    Assert.Equal("CompanyGallery", request.Arguments[2]);
                    Assert.Equal("target-api-key", request.Arguments[3]);
                    Assert.Equal("publisher", request.Arguments[7]);
                    Assert.Equal("target-token", request.Arguments[8]);

                    if (request.Arguments[0].Contains("DependencyModule", StringComparison.OrdinalIgnoreCase))
                    {
                        calls.Add("publish-dependency");
                        dependencyPublished = true;
                    }
                    else if (request.Arguments[0].Contains("OtherDependency", StringComparison.OrdinalIgnoreCase))
                    {
                        calls.Add("publish-other-dependency");
                        otherDependencyPublished = true;
                    }
                    else if (request.Arguments[0].Contains("DependencySupport", StringComparison.OrdinalIgnoreCase))
                    {
                        calls.Add("publish-transitive-dependency");
                    }
                    else
                    {
                        calls.Add("publish-main");
                    }

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
                ApiKey = "target-api-key",
                RepositoryName = "CompanyGallery",
                PublishRequiredModules = true,
                RequiredModuleSourceRepository = "InternalUpstream",
                Repository = new PublishRepositoryConfiguration
                {
                    Name = "CompanyGallery",
                    Credential = new RepositoryCredential
                    {
                        UserName = "publisher",
                        Secret = "target-token"
                    }
                }
            };
            var plan = CreatePlan();
            var buildResult = new ModuleBuildResult(
                stagingPath: stagingRoot,
                manifestPath: manifestPath,
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var result = publisher.Publish(publish, plan, buildResult, Array.Empty<ArtefactBuildResult>());

            Assert.True(result.Succeeded);
            Assert.Equal(
                new[]
                {
                    "find-main",
                    "find-target-dependency",
                    "find-source-dependency",
                    "save-dependency",
                    "find-target-transitive-dependency",
                    "find-target-dependency-package",
                    "publish-dependency",
                    "find-target-dependency",
                    "find-target-other-dependency",
                    "find-source-other-dependency",
                    "save-other-dependency",
                    "find-target-other-dependency-package",
                    "publish-other-dependency",
                    "find-target-other-dependency",
                    "publish-main"
                },
                calls);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void Publish_RejectsRequiredModulePublishingWithPowerShellGet()
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var manifestPath = Path.Combine(stagingRoot, "PSPublishModule.psd1");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '3.0.13'; GUID = 'eb76426a-1992-40a5-82cd-6480f883ef4d'; RootModule = 'PSPublishModule.psm1' }");
            File.WriteAllText(Path.Combine(stagingRoot, "PSPublishModule.psm1"), string.Empty);

            var publisher = new ModulePublisher(
                new NullLogger(),
                new StubPowerShellRunner(new PowerShellRunResult(0, string.Empty, string.Empty, "powershell.exe")));

            var publish = new PublishConfiguration
            {
                Destination = PublishDestination.PowerShellGallery,
                Enabled = true,
                Tool = PublishTool.PowerShellGet,
                ApiKey = "target-api-key",
                RepositoryName = "CompanyGallery",
                PublishRequiredModules = true,
                RequiredModuleSourceRepository = "PSGallery"
            };
            var buildResult = new ModuleBuildResult(
                stagingPath: stagingRoot,
                manifestPath: manifestPath,
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                publisher.Publish(publish, CreatePlan(), buildResult, Array.Empty<ArtefactBuildResult>()));

            Assert.Contains("PublishRequiredModules requires PSResourceGet", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void Publish_RejectsRequiredModulePublishingToPSGallery()
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var manifestPath = Path.Combine(stagingRoot, "PSPublishModule.psd1");
            File.WriteAllText(
                manifestPath,
                """
                @{
                    ModuleVersion = '3.0.13'
                    GUID = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
                    RootModule = 'PSPublishModule.psm1'
                    RequiredModules = @(@{ ModuleName = 'InternalDependency'; ModuleVersion = '1.0.0' })
                }
                """);
            File.WriteAllText(Path.Combine(stagingRoot, "PSPublishModule.psm1"), string.Empty);

            var runner = new StubPowerShellRunner(request =>
            {
                var script = File.ReadAllText(request.ScriptPath!);
                if (script.Contains("Find-PSResource", StringComparison.Ordinal))
                    return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe");

                if (script.Contains("Publish-PSResource", StringComparison.Ordinal))
                    throw new InvalidOperationException("Publish-PSResource should not run when dependency mirroring targets PSGallery.");

                return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe");
            });
            var publisher = new ModulePublisher(new NullLogger(), runner);

            var publish = new PublishConfiguration
            {
                Destination = PublishDestination.PowerShellGallery,
                Enabled = true,
                Tool = PublishTool.PSResourceGet,
                ApiKey = "target-api-key",
                Force = true,
                PublishRequiredModules = true,
                RequiredModuleSourceRepository = "InternalUpstream"
            };
            var buildResult = new ModuleBuildResult(
                stagingPath: stagingRoot,
                manifestPath: manifestPath,
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                publisher.Publish(publish, CreatePlan(), buildResult, Array.Empty<ArtefactBuildResult>()));

            Assert.Contains("Refusing to mirror dependencies to PSGallery", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string[] DecodeLines(string value)
        => Encoding.UTF8.GetString(Convert.FromBase64String(value))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string VisibleRepositoryItem(string name, string version, string? preRelease = null)
        => string.Join("::", new[]
        {
            "PFPSRG::ITEM",
            Encode(name),
            Encode(version),
            Encode("PSGallery"),
            Encode("Przemyslaw Klys"),
            Encode(name),
            Encode(Guid.Empty.ToString()),
            Encode(preRelease ?? string.Empty)
        });

    private static string SaveRepositoryItem(string name, string version)
        => string.Join("::", new[]
        {
            "PFPSRG::SAVE::ITEM",
            Encode(name),
            Encode(version)
        });

    private static ModulePipelinePlan CreatePlan(string resolvedVersion = "3.0.13")
    {
        return new ModulePipelinePlan(
            moduleName: "PSPublishModule",
            projectRoot: @"C:\repo\PSPublishModule",
            expectedVersion: resolvedVersion,
            resolvedVersion: resolvedVersion,
            preRelease: null,
            manifest: null,
            buildSpec: new ModuleBuildSpec
            {
                Name = "PSPublishModule",
                SourcePath = @"C:\repo\PSPublishModule",
                Version = resolvedVersion
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
            actions: Array.Empty<ConfigurationActionSegment>(),
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

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _run;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> run)
        {
            _run = run;
        }

        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_run(request));
        }
    }

    private sealed class FakePowerShellGalleryFeedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("/Packages(", StringComparison.OrdinalIgnoreCase))
            {
                if (uri.Contains("2.5.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                var exactVersion = uri.Contains("2.0.27", StringComparison.OrdinalIgnoreCase)
                    ? "2.0.27"
                    : "3.0.0";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildExactVersion(exactVersion), Encoding.UTF8, "application/atom+xml")
                });
            }

            var body = uri.Contains("$skip=100", StringComparison.OrdinalIgnoreCase)
                ? BuildSecondPage()
                : BuildFirstPage();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/atom+xml")
            });
        }

        private static string BuildExactVersion(string version)
            => $"""
               <?xml version="1.0" encoding="utf-8"?>
               <entry xmlns="http://www.w3.org/2005/Atom"
                      xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                      xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                 <m:properties>
                   <d:Version>{version}</d:Version>
                 </m:properties>
               </entry>
               """;

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
