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
  <ItemGroup Condition="'$(TargetFrameworkIdentifier)' == '.NETFramework' And '$(TargetFrameworkVersion)' >= 'v4.7.2'"><ProjectReference Include="../Two/Two.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([two, one], true, "Release");

        Assert.Equal(["Two", "One"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningHonorsParenthesizedBooleanConditions()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="('$(Configuration)' == 'Release' Or '$(Configuration)' == 'Debug') And ('$(TargetFramework)' == 'net8.0')">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Theory]
    [InlineData("true Or ('x' == )")]
    [InlineData("false And ('x' == )")]
    public void PlanningValidatesMalformedBranchesEvenWhenBooleanResultIsAlreadyKnown(string condition)
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="{condition}"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateService().SortProjectsForPublish([app, shared], true, "Release"));

        Assert.Contains("unsupported MSBuild evaluation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhatIfUsesImportedPackageIdentityAndVersionWithoutRequiringSdkEvaluation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), """
<Project>
  <PropertyGroup>
    <PackageId>Sample.$(MSBuildProjectName)</PackageId>
    <PackageVersion>2.3.4</PackageVersion>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
</Project>
""");
            var sharedDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Shared"));
            File.WriteAllText(Path.Combine(sharedDirectory.FullName, "Shared.csproj"), """
<Project Sdk="Intentionally.Missing.Sdk/999.0.0">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>
""");
            var appDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "App"));
            File.WriteAllText(Path.Combine(appDirectory.FullName, "App.csproj"), """
<Project Sdk="Intentionally.Missing.Sdk/999.0.0">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

            var result = CreateService().Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "Artefacts", "packages"),
                Pack = true,
                Publish = true,
                WhatIf = true,
                PublishApiKey = "unused",
                PublishSource = "https://api.nuget.org/v3/index.json",
                UpdateVersions = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(["Sample.App", "Sample.Shared"], result.Projects.Where(project => project.IsPackable).Select(project => project.PackageId).OrderBy(static id => id, StringComparer.Ordinal));
            Assert.Equal(["Sample.Shared.2.3.4.nupkg", "Sample.App.2.3.4.nupkg"], result.PublishedPackages.Select(Path.GetFileName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
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

    [Fact]
    public void PlanningTreatsUndefinedPropertiesAsEmptyInConditions()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(PowerForgeTestsUndefinedProperty)' == ''"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningIncludesEnvironmentPropertiesUsedByMsBuildConditions()
    {
        const string propertyName = "PowerForgeTestsEnvironmentSwitch";
        var previous = Environment.GetEnvironmentVariable(propertyName);
        try
        {
            Environment.SetEnvironmentVariable(propertyName, "true");
            using var workspace = new PublishOrderWorkspace();
            var shared = workspace.AddProject("Shared");
            var app = workspace.AddProject("App");
            File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(PowerForgeTestsEnvironmentSwitch)' == 'true'"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

            var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

            Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
        }
        finally
        {
            Environment.SetEnvironmentVariable(propertyName, previous);
        }
    }

    [Fact]
    public void PlanningPreservesFourPartTargetPlatformVersions()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0-windows10.0.19041.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(TargetPlatformVersion)' == '10.0.19041.0'"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningEvaluatesWildcardImportsInDeterministicOrderAndAllowsEmptyMatches()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var imports = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(app.CsprojPath)!, "imports"));
        File.WriteAllText(Path.Combine(imports.FullName, "01-default.props"), "<Project><PropertyGroup><UseShared>false</UseShared></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(imports.FullName, "02-override.props"), "<Project><PropertyGroup><UseShared>true</UseShared></PropertyGroup></Project>");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="imports/**/*.props" />
  <Import Project="optional/*.props" />
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(UseShared)' == 'true'"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([app, shared], true, "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningMaterializesWildcardIncludesBeforeApplyingExcludes()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var legacy = workspace.AddProject("Legacy");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Shared/*.csproj;../Legacy/*.csproj" Exclude="../Legacy/Legacy.csproj" /></ItemGroup>
</Project>
""");
        File.WriteAllText(legacy.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../App/App.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([legacy, app, shared], true, "Release");

        Assert.Equal(["Shared", "App", "Legacy"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningPreservesWildcardExcludesThroughItemExpressions()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var legacy = workspace.AddProject("Legacy");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <LocalProject Include="../Shared/*.csproj;../Legacy/*.csproj" Exclude="../Legacy/Legacy.csproj" />
    <ProjectReference Include="@(LocalProject)" />
  </ItemGroup>
</Project>
""");
        File.WriteAllText(legacy.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../App/App.csproj" /></ItemGroup>
</Project>
""");

        var ordered = CreateService().SortProjectsForPublish([legacy, app, shared], true, "Release");

        Assert.Equal(["Shared", "App", "Legacy"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void PlanningUsesPendingProjectContentsForVersionBasedPackageReferences()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared", version: "2.0.0");
        var app = workspace.AddProject("App", version: "2.0.0");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><Version>1.0.0</Version></PropertyGroup>
  <ItemGroup><PackageReference Include="Shared" Version="[$(Version)]" /></ItemGroup>
</Project>
""");
        var planned = File.ReadAllText(app.CsprojPath).Replace("<Version>1.0.0</Version>", "<Version>2.0.0</Version>", StringComparison.Ordinal);

        var ordered = CreateService().SortProjectsForPublish(
            [app, shared],
            true,
            "Release",
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.GetFullPath(app.CsprojPath)] = planned
            });

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void WhatIfOrdersPackagesFromThePendingVersionUpdateGraph()
    {
        using var workspace = new PublishOrderWorkspace();
        const string sharedPackageId = "PowerForge.Tests.PendingGraph.Shared";
        const string appPackageId = "PowerForge.Tests.PendingGraph.App";
        var shared = workspace.AddProject("Shared", packageId: sharedPackageId);
        var app = workspace.AddProject("App", packageId: appPackageId);
        File.WriteAllText(shared.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><PackageId>PowerForge.Tests.PendingGraph.Shared</PackageId><Version>1.0.0</Version><IsPackable>true</IsPackable></PropertyGroup>
</Project>
""");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><PackageId>PowerForge.Tests.PendingGraph.App</PackageId><Version>1.0.0</Version><IsPackable>true</IsPackable></PropertyGroup>
  <ItemGroup><PackageReference Include="PowerForge.Tests.PendingGraph.Shared" Version="[$(Version)]" /></ItemGroup>
</Project>
""");
        var root = Path.GetDirectoryName(Path.GetDirectoryName(app.CsprojPath)!)!;

        var result = CreateService().Execute(new DotNetRepositoryReleaseSpec
        {
            RootPath = root,
            Configuration = "Release",
            OutputPath = Path.Combine(root, "Artefacts", "packages"),
            ExpectedVersion = "2.0.0",
            UpdateVersions = true,
            Pack = true,
            Publish = true,
            WhatIf = true,
            PublishApiKey = "unused",
            PublishSource = "https://api.nuget.org/v3/index.json"
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(
            [sharedPackageId + ".2.0.0.nupkg", appPackageId + ".2.0.0.nupkg"],
            result.PublishedPackages.Select(Path.GetFileName));
    }

    [Fact]
    public void PlanningUsesSdkDefaultVersionPrefixWhenOnlyVersionSuffixIsDeclared()
    {
        using var workspace = new PublishOrderWorkspace();
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><VersionSuffix>beta.1</VersionSuffix><IsPackable>true</IsPackable></PropertyGroup>
</Project>
""");

        var result = CreateService().Execute(new DotNetRepositoryReleaseSpec
        {
            RootPath = Path.GetDirectoryName(Path.GetDirectoryName(app.CsprojPath)!)!,
            Configuration = "Release",
            Pack = false,
            Publish = false,
            WhatIf = true,
            UpdateVersions = false
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("1.0.0-beta.1", Assert.Single(result.Projects).NewVersion);
    }

    private static DotNetRepositoryReleaseService CreateService() => new(new NullLogger());
}
