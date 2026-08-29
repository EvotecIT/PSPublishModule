using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    public void Execute_WhatIfRefreshesVersionsChangedByVersionBindings()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-binding-plan-" + Guid.NewGuid().ToString("N")));
        try
        {
            var anchorDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Anchor"));
            File.WriteAllText(Path.Combine(anchorDirectory.FullName, "Anchor.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup></Project>");
            var dependentDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Dependent"));
            var dependentPath = Path.Combine(dependentDirectory.FullName, "Dependent.csproj");
            var dependentSource =
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup></Project>";
            File.WriteAllText(dependentPath, dependentSource);
            var outputPath = Path.Combine(root.FullName, "packages");

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Anchor"] = "2.0.0"
                },
                UpdateVersions = true,
                WhatIf = true,
                Pack = true,
                OutputPath = outputPath,
                VersionBindings =
                [
                    new ProjectVersionBinding
                    {
                        Path = "Dependent/Dependent.csproj",
                        Project = "Anchor",
                        Pattern = @"(?<=<VersionPrefix>)\d+\.\d+\.\d+(?=</VersionPrefix>)"
                    }
                ]
            });

            Assert.True(result.Success, result.ErrorMessage);
            var dependent = Assert.Single(result.Projects, project => project.ProjectName == "Dependent");
            Assert.Equal("2.0.0", dependent.NewVersion);
            Assert.Equal("2.0.0", result.ResolvedVersionsByProject["Dependent"]);
            Assert.Contains(dependent.Packages, path => Path.GetFileName(path) == "Dependent.2.0.0.nupkg");
            Assert.Equal(dependentSource, File.ReadAllText(dependentPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SortProjectsForPublish_PlanningFailsClosedWhenCentralTransitivePinningIsEnabled()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
</Project>
""");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger())
                .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release"));

        Assert.Contains("CentralPackageTransitivePinningEnabled", exception.Message, StringComparison.Ordinal);
        Assert.Contains("transitive dependencies", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<PackageId>$(PackagePrefix)Sdk</PackageId>", "Company.Sdk")]
    [InlineData("<AssemblyName>$(PackagePrefix)Sdk</AssemblyName>", "Company.Sdk")]
    public void Execute_RealReleaseResolvesPackageIdentityFromEvaluatedMetadata(string identityProperty, string expectedPackageId)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-package-id-" + Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sdk"));
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "identity.props"),
                $"<Project><PropertyGroup><PackagePrefix>Company.</PackagePrefix>{identityProperty}</PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "Sdk.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="identity.props" />
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
""");

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                UpdateVersions = false,
                Pack = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(expectedPackageId, Assert.Single(result.Projects).PackageId);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test temp files.
        }
    }
}
