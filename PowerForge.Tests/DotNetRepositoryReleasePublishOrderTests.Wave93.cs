using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    public void SortProjectsForPublish_PlanningSeedsBuiltInSdkProperties()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(UsingMicrosoftNETSdk)' == 'true'">
    <PackageReference Include="Shared" Version="[1.0.0]" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningEvaluatesCurrentItemMetadataConditions()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Shared" Version="[1.0.0]">
      <PrivateAssets Condition="'%(PackageReference.Identity)' == 'Shared'">all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningPreservesMetadataWhenCloningItemLists()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <Dependencies Include="Shared" Version="[1.0.0]" PrivateAssets="all" />
    <PackageReference Include="@(Dependencies)" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void Execute_DoesNotEvaluatePackageIdentityForNonPackableProjects()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-nonpackable-discovery-" + Guid.NewGuid().ToString("N")));
        try
        {
            var appDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "App"));
            File.WriteAllText(Path.Combine(appDirectory.FullName, "App.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><IsPackable>true</IsPackable></PropertyGroup>
</Project>
""");
            var utilityDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Utility"));
            File.WriteAllText(Path.Combine(utilityDirectory.FullName, "Utility.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="missing.props" />
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><IsPackable>false</IsPackable></PropertyGroup>
</Project>
""");

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                Pack = false,
                UpdateVersions = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            var utility = Assert.Single(result.Projects, project => project.ProjectName == "Utility");
            Assert.False(utility.IsPackable);
            Assert.Equal("Utility", utility.PackageId);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
