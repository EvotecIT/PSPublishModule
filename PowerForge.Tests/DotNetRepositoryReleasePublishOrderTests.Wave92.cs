using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_RefreshesPackageIdentityAfterVersionUpdates(bool whatIf)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-package-id-refresh-" + Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "App"));
            var projectPath = Path.Combine(projectDirectory.FullName, "App.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Version>1.0.0</Version>
    <PackageId>Package.$(Version)</PackageId>
  </PropertyGroup>
</Project>
""");

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["App"] = "2.0.0"
                },
                UpdateVersions = true,
                WhatIf = whatIf,
                Pack = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("Package.2.0.0", Assert.Single(result.Projects).PackageId);
            var content = File.ReadAllText(projectPath);
            Assert.Equal(!whatIf, content.Contains("<Version>2.0.0</Version>", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteWave92Directory(root);
        }
    }

    [Fact]
    public void SortProjectsForPublish_PlanningHonorsDisabledDirectoryPackagesPropsImport()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var root = Directory.GetParent(Path.GetDirectoryName(app.CsprojPath)!)!.FullName;
        File.WriteAllText(Path.Combine(root, "Directory.Build.props"),
            "<Project><PropertyGroup><ImportDirectoryPackagesProps>false</ImportDirectoryPackagesProps></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), """
<Project>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'App'">
    <PackageReference Include="Shared" Version="[1.0.0]" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningHonorsRedirectedDirectoryPackagesPropsPath()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var root = Directory.GetParent(Path.GetDirectoryName(app.CsprojPath)!)!.FullName;
        File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
<Project>
  <PropertyGroup>
    <DirectoryPackagesPropsPath>$(MSBuildProjectDirectory)/../Custom.Packages.props</DirectoryPackagesPropsPath>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), "<Project />");
        File.WriteAllText(Path.Combine(root, "Custom.Packages.props"), """
<Project>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'App'">
    <PackageReference Include="Shared" Version="[1.0.0]" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Theory]
    [InlineData("props")]
    [InlineData("targets")]
    public void SortProjectsForPublish_PlanningEvaluatesRestoredPackageBuildImports(string extension)
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var appDirectory = Path.GetDirectoryName(app.CsprojPath)!;
        var intermediateDirectory = Directory.CreateDirectory(Path.Combine(appDirectory, "obj"));
        var packageBuildFile = Path.Combine(intermediateDirectory.FullName, "package-build." + extension);
        File.WriteAllText(packageBuildFile, """
<Project>
  <ItemGroup>
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");
        File.WriteAllText(
            Path.Combine(intermediateDirectory.FullName, "App.csproj.nuget.g." + extension),
            $"<Project><Import Project=\"package-build.{extension}\" /></Project>");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    private static void TryDeleteWave92Directory(DirectoryInfo directory)
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
