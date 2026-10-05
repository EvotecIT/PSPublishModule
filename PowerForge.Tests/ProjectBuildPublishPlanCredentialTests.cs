using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildPublishPlanCredentialTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Execute_PublishEnabledPlan_DoesNotReadPublicationSecret()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-publish-plan", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample"));
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "Sample.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.Package</PackageId>
                    <VersionPrefix>1.2.3</VersionPrefix>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
            Directory.CreateDirectory(Path.Combine(root.FullName, "feed"));
            var configPath = Path.Combine(root.FullName, "project.build.json");
            File.WriteAllText(configPath, """
                {
                  "RootPath": ".",
                  "OutputPath": "artifacts/packages",
                  "Build": false,
                  "UpdateVersions": false,
                  "CreateReleaseZip": false,
                  "PublishNuget": true,
                  "PublishGitHub": false,
                  "NugetSource": ["feed"],
                  "PublishSource": "feed",
                  "PublishApiKeyFilePath": "publish.secret"
                }
                """);
            var secretPath = Path.Combine(root.FullName, "publish.secret");
            File.WriteAllText(secretPath, "test-publication-secret");
            // A read would fail: planning must leave publication-only credentials unopened.
            using var secretLock = new FileStream(secretPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = new ProjectBuildHostService().Execute(new ProjectBuildHostRequest
            {
                ConfigPath = configPath,
                PlanOnly = true,
                ExecuteBuild = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            var release = Assert.IsType<DotNetRepositoryReleaseResult>(result.Result.Release);
            var project = Assert.Single(release.Projects);
            Assert.Equal("1.2.3", project.NewVersion);
            var package = Assert.Single(release.PublishedPackages);
            Assert.Equal("Sample.Package.1.2.3.nupkg", Path.GetFileName(package));
            Assert.False(File.Exists(package));
            Assert.False(Directory.Exists(Path.Combine(root.FullName, "artifacts")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ExecuteNuGetPublishing_RequiresKeyBeforeActualPublication(string? apiKey)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-publish-plan", Guid.NewGuid().ToString("N")));
        try
        {
            var feed = Directory.CreateDirectory(Path.Combine(root.FullName, "feed"));
            var packagePath = Path.Combine(root.FullName, "Sample.Package.1.2.3.nupkg");
            File.WriteAllText(packagePath, "preflight-file-fixture");
            var project = new DotNetRepositoryProjectResult
            {
                ProjectName = "Sample.Package",
                PackageId = "Sample.Package",
                NewVersion = "1.2.3",
                Packages = { packagePath }
            };
            var attemptedPublication = false;
            var service = new DotNetRepositoryReleaseService(new NullLogger(), null, null,
                pushPackage: (_, _, _, _, _, _) => throw new InvalidOperationException("Publication must be blocked."));
            var release = new DotNetRepositoryReleaseResult { Success = true };

            var stopped = service.ExecuteNuGetPublishing(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Publish = true,
                WhatIf = false,
                PublishApiKey = apiKey,
                PublishSource = feed.FullName,
                RemotePublishAttempted = () => attemptedPublication = true
            }, release, [project], root.FullName, null, null);

            Assert.True(stopped);
            Assert.False(release.Success);
            Assert.Equal("PublishApiKey is required when Publish is enabled.", release.ErrorMessage);
            Assert.False(attemptedPublication);
            Assert.Empty(release.PublishedPackages);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
