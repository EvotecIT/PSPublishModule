using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    public void SortProjectsForPublish_PlanningExpandsCurrentMetadataDuringUpdates()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Shared" Version="[1.0.0]" PrivateAssets="all" />
    <PackageReference Update="Shared">
      <PrivateAssets>%(PackageReference.PrivateAssets);contentfiles</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
""");
        File.WriteAllText(shared.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="App" Version="[1.0.0]" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void Execute_UsesExpandedCustomNuspecIdForPlannedPackages()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-nuspec-identity-" + Guid.NewGuid().ToString("N")));
        try
        {
            var producerDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Producer"));
            File.WriteAllText(Path.Combine(producerDirectory.FullName, "Producer.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Version>1.0.0</Version>
    <NuspecFile>Producer.nuspec</NuspecFile>
    <NuspecProperties>Flavor=Core;ReleaseVersion=2.0.0</NuspecProperties>
  </PropertyGroup>
</Project>
""");
            File.WriteAllText(Path.Combine(producerDirectory.FullName, "Producer.nuspec"), """
<package><metadata><id>Zeta.$Flavor$</id><version>$ReleaseVersion$</version></metadata></package>
""");
            var consumerDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Consumer"));
            File.WriteAllText(Path.Combine(consumerDirectory.FullName, "Consumer.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><Version>1.0.0</Version></PropertyGroup>
  <ItemGroup><PackageReference Include="Zeta.Core" Version="[2.0.0]" /></ItemGroup>
</Project>
""");

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "Artefacts", "packages"),
                WhatIf = true,
                Pack = true,
                Publish = true,
                PublishApiKey = "unused",
                PublishSource = "https://api.nuget.org/v3/index.json",
                SkipDuplicate = true,
                UpdateVersions = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(["Zeta.Core.2.0.0.nupkg", "Consumer.1.0.0.nupkg"], result.PublishedPackages.Select(Path.GetFileName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SortProjectsForPublish_PlanningUnescapesProjectReferenceItemSpecs()
    {
        using var workspace = new PublishOrderWorkspace();
        var library = workspace.AddProject("My Lib", packageId: "Library");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../My%20Lib/My%20Lib.csproj" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, library], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Library", "App"], ordered.Select(project => project.PackageId));
    }
}
