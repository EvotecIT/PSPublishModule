using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    public void SortProjectsForPublish_PlanningSeedsReservedProjectIdentityProperties()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(MSBuildProjectFile)' == 'App.csproj' And '$(MSBuildProjectExtension)' == '.csproj' And '$(MSBuildProjectDirectoryNoRoot)' != ''">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningScopesReservedImportedFileIdentityProperties()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var appDirectory = Path.GetDirectoryName(app.CsprojPath)!;
        File.WriteAllText(Path.Combine(appDirectory, "identity.props"), """
<Project>
  <ItemGroup Condition="'$(MSBuildThisFile)' == 'identity.props' And '$(MSBuildThisFileExtension)' == '.props' And '$(MSBuildThisFileDirectoryNoRoot)' != ''">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="identity.props" />
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Theory]
    [InlineData("$([System.String]::Copy('net9.0'))")]
    [InlineData("@(PlannedFramework)")]
    public void SortProjectsForPublish_PlanningFailsClosedOnUnsupportedTargetFrameworkExpressions(string targetFrameworks)
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFrameworks>{targetFrameworks}</TargetFrameworks></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger())
                .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release"));

        Assert.Contains("target frameworks", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsupported MSBuild expression", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
