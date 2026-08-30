using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    public void SortProjectsForPublish_ProjectTargetFrameworkOverridesEnvironmentDefault()
    {
        using var workspace = new PublishOrderWorkspace();
        var netEight = workspace.AddProject("NetEight");
        var netNine = workspace.AddProject("NetNine");
        var app = workspace.AddProject("App");
        File.WriteAllText(netEight.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../App/App.csproj" /></ItemGroup>
</Project>
""");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
    <ProjectReference Include="../NetEight/NetEight.csproj" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
    <ProjectReference Include="../NetNine/NetNine.csproj" />
  </ItemGroup>
</Project>
""");
        var previous = Environment.GetEnvironmentVariable("TargetFramework");
        try
        {
            Environment.SetEnvironmentVariable("TargetFramework", "net8.0");
            var ordered = new DotNetRepositoryReleaseService(new NullLogger())
                .SortProjectsForPublish([app, netNine, netEight], usePlannedProjectGraph: true, configuration: "Release");

            Assert.Equal(["NetNine", "App", "NetEight"], ordered.Select(project => project.PackageId));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TargetFramework", previous);
        }
    }

    [Fact]
    public void SortProjectsForPublish_PackageIdsThatLookLikeProjectPathsRemainCaseInsensitive()
    {
        using var workspace = new PublishOrderWorkspace();
        var producer = workspace.AddProject("Producer", packageId: "Foo.csproj");
        var consumer = workspace.AddProject("Consumer");
        File.WriteAllText(consumer.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="foo.csproj" Version="[1.0.0]" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([consumer, producer], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Foo.csproj", "Consumer"], ordered.Select(project => project.PackageId));
    }
}
