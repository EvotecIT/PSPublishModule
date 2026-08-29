using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    public void PlanningDerivesStandardFrameworkPropertiesBeforeEvaluatingReferences()
    {
        using var workspace = new PublishOrderWorkspace();
        var one = workspace.AddProject("One");
        var two = workspace.AddProject("Two");
        File.WriteAllText(one.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFrameworks>net8.0;net472</TargetFrameworks></PropertyGroup>
  <ItemGroup Condition="'$(TargetFrameworkIdentifier)' == '.NETCoreApp' And '$(TargetFrameworkVersion)' >= 'v8.0'"><ProjectReference Include="../Two/Two.csproj" /></ItemGroup>
</Project>
""");
        File.WriteAllText(two.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFrameworks>net8.0;net472</TargetFrameworks></PropertyGroup>
  <ItemGroup Condition="'$(TargetFrameworkIdentifier)' == '.NETFramework'"><ProjectReference Include="../One/One.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([two, one], true, "Release");

        Assert.Equal(["One", "Two"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningHonorsConditionsOnItemMetadata()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Shared" Version="[1.0.0]"><PrivateAssets Condition="'$(Configuration)' == 'Debug'">all</PrivateAssets></PackageReference></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningEvaluatesExistsBeforeApplyingConditionalProperties()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><UseShared>true</UseShared></PropertyGroup>
  <PropertyGroup Condition="Exists('optional.props')"><UseShared>false</UseShared></PropertyGroup>
  <ItemGroup Condition="'$(UseShared)' == 'true'"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningFailsClosedForUnsupportedConditions()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="HasTrailingSlash('path')"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var exception = Assert.Throws<InvalidOperationException>(() => CreateService().SortProjectsForPublish([app, shared], true, "Release"));

        Assert.Contains("unsupported MSBuild evaluation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanningHonorsSuppressDependenciesWhenPackingPerFramework()
    {
        using var workspace = new PublishOrderWorkspace();
        var one = workspace.AddProject("One");
        var two = workspace.AddProject("Two");
        File.WriteAllText(one.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Two/Two.csproj" /></ItemGroup>
</Project>
""");
        File.WriteAllText(two.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../One/One.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([two, one], true, "Release");

        Assert.Equal(["One", "Two"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningReadsDependenciesFromCustomNuspec()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var appDirectory = Path.GetDirectoryName(app.CsprojPath)!;
        File.WriteAllText(Path.Combine(appDirectory, "App.nuspec"), """
<package><metadata><id>App</id><version>1.0.0</version><dependencies><group targetFramework="net8.0"><dependency id="$SharedId$" version="[$version$]" /></group></dependencies></metadata></package>
""");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><NuspecFile>App.nuspec</NuspecFile><NuspecProperties>SharedId=Shared</NuspecProperties></PropertyGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningAppliesWildcardRemoveAndUpdateSemantics()
    {
        using var workspace = new PublishOrderWorkspace();
        var one = workspace.AddProject("One");
        var two = workspace.AddProject("Two");
        File.WriteAllText(one.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Two/Two.csproj" /><ProjectReference Remove="../Two/*.csproj" /></ItemGroup>
</Project>
""");
        File.WriteAllText(two.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../One/One.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([two, one], true, "Release");

        Assert.Equal(["One", "Two"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningExpandsPlainItemExpressionsInReferenceIncludes()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><LocalProjects Include="../Shared/Shared.csproj" /><ProjectReference Include="@(LocalProjects)" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningResolvesImportedItemPathsFromMainProjectDirectory()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var appDirectory = Path.GetDirectoryName(app.CsprojPath)!;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(appDirectory, "build"));
        File.WriteAllText(Path.Combine(buildDirectory.FullName, "dependencies.props"), "<Project><ItemGroup><ProjectReference Include=\"../Shared/Shared.csproj\" /></ItemGroup></Project>");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$(MSBuildThisFileDirectory)build/dependencies.props" />
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningAppliesItemDefinitionMetadataToReferences()
    {
        using var workspace = new PublishOrderWorkspace();
        var one = workspace.AddProject("One");
        var two = workspace.AddProject("Two");
        File.WriteAllText(one.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemDefinitionGroup><PackageReference><PrivateAssets>all</PrivateAssets></PackageReference></ItemDefinitionGroup>
  <ItemGroup><PackageReference Include="Two" Version="[1.0.0]" /></ItemGroup>
</Project>
""");
        File.WriteAllText(two.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="One" Version="[1.0.0]" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([two, one], true, "Release");

        Assert.Equal(["One", "Two"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningHonorsExcludeAndTreatAsPackageReferenceMetadata()
    {
        using var workspace = new PublishOrderWorkspace();
        var one = workspace.AddProject("One");
        var two = workspace.AddProject("Two");
        File.WriteAllText(one.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Two/Two.csproj" Exclude="../Two/*.csproj" /></ItemGroup>
</Project>
""");
        File.WriteAllText(two.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../One/One.csproj" TreatAsPackageReference="false" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([two, one], true, "Release");

        Assert.Equal(["One", "Two"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningTreatsSelectedFrameworkAndConfigurationAsGlobalProperties()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><Configuration>Debug</Configuration><TargetFrameworks>net8.0;net472</TargetFrameworks><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(Configuration)' == 'Release' And '$(TargetFramework)' == 'net472'"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningMatchesWildcardIncludesAgainstSelectedProjects()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Shared/*.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningAppliesWildcardUpdatesToExistingReferences()
    {
        using var workspace = new PublishOrderWorkspace();
        var one = workspace.AddProject("One");
        var two = workspace.AddProject("Two");
        File.WriteAllText(one.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Two/Two.csproj" /><ProjectReference Update="../Two/*.csproj"><PrivateAssets>all</PrivateAssets></ProjectReference></ItemGroup>
</Project>
""");
        File.WriteAllText(two.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../One/One.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([two, one], true, "Release");

        Assert.Equal(["One", "Two"], ordered.Select(project => project.PackageId));
    }

    private static DotNetRepositoryReleaseService CreateService() => new(new NullLogger());
}
