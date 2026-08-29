using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    public void ExecuteNuGetPublishing_ContinuesIndependentPackagesButBlocksConsumersAfterDependencyFailure()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App", ["Shared"]);
        var independent = workspace.AddProject("Independent");
        var attempts = new List<string>();
        var root = Path.GetDirectoryName(Path.GetDirectoryName(shared.CsprojPath)!)!;
        var feed = Directory.CreateDirectory(Path.Combine(root, "feed"));
        var service = new DotNetRepositoryReleaseService(
            new NullLogger(),
            signPackages: null,
            getCertificateSha256: null,
            pushPackage: (packagePath, _, _, _, _, _) =>
            {
                var packageName = Path.GetFileName(packagePath);
                attempts.Add(packageName);
                return new DotNetRepositoryReleaseService.PackagePushResult
                {
                    Outcome = packageName.StartsWith("Shared.", StringComparison.OrdinalIgnoreCase)
                        ? DotNetRepositoryReleaseService.PackagePushOutcome.Failed
                        : DotNetRepositoryReleaseService.PackagePushOutcome.Published,
                    Message = "simulated push result"
                };
            });
        var result = new DotNetRepositoryReleaseResult();
        var spec = new DotNetRepositoryReleaseSpec
        {
            RootPath = root,
            Configuration = "Release",
            Publish = true,
            PublishApiKey = "test-key",
            PublishSource = feed.FullName,
            PublishFailFast = false,
            SkipDuplicate = true
        };

        var stopped = service.ExecuteNuGetPublishing(spec, result, [app, shared, independent], root, null, null);

        Assert.False(stopped);
        Assert.False(result.Success);
        Assert.Contains(attempts, package => package.StartsWith("Shared.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(attempts, package => package.StartsWith("Independent.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(attempts, package => package.StartsWith("App.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(independent.Packages[0], result.PublishedPackages, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(app.Packages[0], result.FailedPackages, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Shared", app.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecuteNuGetPublishing_UsesReleaseForBlankPlannedConfiguration()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(Configuration)' == 'Release'">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");
        var root = Path.GetDirectoryName(Path.GetDirectoryName(shared.CsprojPath)!)!;
        var result = new DotNetRepositoryReleaseResult();
        var spec = new DotNetRepositoryReleaseSpec
        {
            RootPath = root,
            Configuration = "   ",
            Publish = true,
            PublishApiKey = "test-key",
            PublishSource = Path.Combine(root, "feed"),
            WhatIf = true,
            SkipDuplicate = true
        };

        var stopped = new DotNetRepositoryReleaseService(new NullLogger())
            .ExecuteNuGetPublishing(spec, result, [app, shared], root, null, null);

        Assert.False(stopped);
        Assert.Equal([shared.Packages[0], app.Packages[0]], result.PublishedPackages);
    }

    [Fact]
    public void SortProjectsForPublish_PlanningExpandsUndefinedPropertiesAtAssignmentTime()
    {
        using var workspace = new PublishOrderWorkspace();
        var core = workspace.AddProject("Core");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <BaseName>$(OptionalPrefix)Core</BaseName>
    <OptionalPrefix>Company.</OptionalPrefix>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="$(BaseName)" Version="[1.0.0]" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, core], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Core", "App"], ordered.Select(project => project.PackageId));
    }
}
