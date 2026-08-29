using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    public void SortProjectsForPublish_PlanningUsesFilesystemCasingForPathItemOperations()
    {
        using var workspace = new PublishOrderWorkspace();
        var library = workspace.AddProject("Library");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <DependencyPath Include="../Library/Library.csproj" />
    <DependencyPath Remove="../library/library.csproj" />
    <ProjectReference Include="@(DependencyPath)" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, library], usePlannedProjectGraph: true, configuration: "Release");

        var caseSensitive = FrameworkCompatibility.GetPathStringComparison(Path.GetDirectoryName(app.CsprojPath)!) == StringComparison.Ordinal;
        Assert.Equal(caseSensitive ? ["Library", "App"] : ["App", "Library"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningPreservesEnvironmentTargetFramework()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");
        var previous = Environment.GetEnvironmentVariable("TargetFramework");
        try
        {
            Environment.SetEnvironmentVariable("TargetFramework", "net8.0");
            var ordered = new DotNetRepositoryReleaseService(new NullLogger())
                .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

            Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TargetFramework", previous);
        }
    }
}
