using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "DotNetPublishPrGate")]
public sealed partial class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    public void SortProjectsForPublish_PublishesDependenciesBeforeConsumers()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("IntelligenceX.Shared");
        var sdk = workspace.AddProject("IntelligenceX", ["IntelligenceX.Shared"]);

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([sdk, shared]);

        Assert.Equal(["IntelligenceX.Shared", "IntelligenceX"], ordered.Select(project => project.ProjectName));
    }

    [Fact]
    public void SortProjectsForPublish_OrdersDiamondGraphWithoutPublishingConsumersEarly()
    {
        using var workspace = new PublishOrderWorkspace();
        var app = workspace.AddProject("App", ["Feature.One", "Feature.Two"]);
        var featureTwo = workspace.AddProject("Feature.Two", ["Core"]);
        var core = workspace.AddProject("Core");
        var featureOne = workspace.AddProject("Feature.One", ["Core"]);

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, featureTwo, core, featureOne])
            .Select(project => project.ProjectName)
            .ToArray();

        Assert.Equal("Core", ordered[0]);
        Assert.True(Array.IndexOf(ordered, "Feature.One") < Array.IndexOf(ordered, "App"));
        Assert.True(Array.IndexOf(ordered, "Feature.Two") < Array.IndexOf(ordered, "App"));
    }

    [Fact]
    public void SortProjectsForPublish_UsesPackedDependencyMetadataInsteadOfProjectXml()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App", ["Shared"]);
        File.WriteAllText(app.CsprojPath, "<Project><Import Project=\"missing.props\" /></Project>");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared]);

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_IsDeterministicForIndependentPackages()
    {
        using var workspace = new PublishOrderWorkspace();
        var zulu = workspace.AddProject("Zulu");
        var alpha = workspace.AddProject("Alpha");
        var middle = workspace.AddProject("Middle");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([zulu, middle, alpha]);

        Assert.Equal(["Alpha", "Middle", "Zulu"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_RejectsDependencyCycles()
    {
        using var workspace = new PublishOrderWorkspace();
        var one = workspace.AddProject("One", ["Two"]);
        var two = workspace.AddProject("Two", ["One"]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger()).SortProjectsForPublish([one, two]));

        Assert.Contains("dependency cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Publishing stopped before any package was pushed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SortProjectsForPublish_RejectsMissingOrMalformedPackageMetadata()
    {
        using var workspace = new PublishOrderWorkspace();
        var missing = workspace.AddProject("Missing");
        File.Delete(missing.Packages[0]);

        var missingException = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger()).SortProjectsForPublish([missing, workspace.AddProject("Other")]));
        Assert.Contains("does not exist", missingException.Message, StringComparison.OrdinalIgnoreCase);

        var malformed = workspace.AddProject("Malformed", createNuspec: false);
        var malformedException = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger()).SortProjectsForPublish([malformed, workspace.AddProject("Another")]));
        Assert.Contains("exactly one .nuspec", malformedException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SortProjectsForPublish_RejectsDuplicatePackageIds()
    {
        using var workspace = new PublishOrderWorkspace();
        var first = workspace.AddProject("First", packageId: "Duplicate");
        var second = workspace.AddProject("Second", packageId: "Duplicate");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger()).SortProjectsForPublish([first, second]));

        Assert.Contains("more than one selected project", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SortProjectsForPublish_IgnoresDependencyRangesThatDoNotTargetSelectedVersion()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared", version: "2.0.0");
        var app = workspace.AddProject("App", ["Shared"], dependencyVersion: "[1.0.0]");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app]);

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_RejectsCrossFrameworkCyclesWithoutAValidGlobalOrder()
    {
        using var workspace = new PublishOrderWorkspace();
        var one = workspace.AddProject("One", dependencyGroups: new Dictionary<string, string[]> { ["net8.0"] = ["Two"] });
        var two = workspace.AddProject("Two", dependencyGroups: new Dictionary<string, string[]> { ["net472"] = ["One"] });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger()).SortProjectsForPublish([two, one]));

        Assert.Contains("dependency cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SortProjectsForPublish_PlansFromProjectXmlWithoutRunningDotNetAndHonorsConfiguration()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="missing.props" Condition="Exists('missing.props')" />
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(Configuration)' == 'Release'">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");
        var service = new DotNetRepositoryReleaseService(new NullLogger());

        var release = service.SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");
        var debug = service.SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Debug");

        Assert.Equal(["Shared", "App"], release.Select(project => project.PackageId));
        Assert.Equal(["App", "Shared"], debug.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningFailsClosedWhenActiveImportIsMissing()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, "<Project><Import Project=\"missing.props\" /></Project>");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger())
                .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release"));

        Assert.Contains("does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing.props", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SortProjectsForPublish_PlanningExcludesPrivatePackageReferences()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Shared" Version="[1.0.0]" PrivateAssets="all" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningExpandsPackageIdentityAndKeepsUnresolvedVersionConservatively()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <SharedPackage>Shared</SharedPackage>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$(SharedPackage)" Version="$(UnresolvedSharedVersion)" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_RejectsFallbackAndFrameworkEdgesThatFormAGlobalCycle()
    {
        using var workspace = new PublishOrderWorkspace();
        var app = workspace.AddProject("App", dependencyGroups: new Dictionary<string, string[]>
        {
            [string.Empty] = ["Shared"],
            ["net8.0"] = []
        });
        var shared = workspace.AddProject("Shared", dependencyGroups: new Dictionary<string, string[]>
        {
            ["net8.0"] = ["App"]
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger()).SortProjectsForPublish([shared, app]));

        Assert.Contains("dependency cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SortProjectsForPublish_PlanningUsesProjectPropertiesInItemConditions()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><UseShared>false</UseShared></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" Condition="'$(UseShared)' == 'true'" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningExpandsPropertyBasedTargetFrameworks()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><SupportedFrameworks>net8.0;net472</SupportedFrameworks><TargetFrameworks>$(SupportedFrameworks)</TargetFrameworks></PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningResolvesMsBuildThisFileDirectoryImports()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        var buildDirectory = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(app.CsprojPath)!, "build"));
        File.WriteAllText(Path.Combine(buildDirectory.FullName, "dependencies.props"), """
<Project><ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup></Project>
""");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$(MSBuildThisFileDirectory)build/dependencies.props" />
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningDoesNotCreateEdgesFromPackageReferenceUpdates()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Update="Shared" Version="[1.0.0]" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningNormalizesMsBuildProjectReferenceSeparators()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="..\Shared\Shared.csproj" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningEvaluatesImportsInline()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(app.CsprojPath)!, "shared.props"),
            "<Project><PropertyGroup><UseShared>true</UseShared></PropertyGroup></Project>");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="shared.props" />
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><UseShared>false</UseShared></PropertyGroup>
  <ItemGroup Condition="'$(UseShared)' == 'true'"><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningUsesCentralPackageVersions()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared", version: "2.0.0");
        var app = workspace.AddProject("App");
        workspace.WriteRootFile("Directory.Packages.props", """
<Project>
  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup><PackageVersion Include="Shared" Version="[1.0.0]" /></ItemGroup>
</Project>
""");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Shared" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_AcceptsNestedNuspecContentWhenRootManifestIsUnique()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App", ["Shared"]);
        workspace.AddArchiveText(app.Packages[0], "tools/example.nuspec", "<package />");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared]);

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningIgnoresItemsInsideTargets()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <Target Name="AfterClean" AfterTargets="Clean">
    <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
  </Target>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    public void SortProjectsForPublish_PlanningHonorsImportGroupConditions()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(app.CsprojPath)!, "debug.props"),
            "<Project><ItemGroup><ProjectReference Include=\"../Shared/Shared.csproj\" /></ItemGroup></Project>");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ImportGroup Condition="'$(Configuration)' == 'Debug'"><Import Project="debug.props" /></ImportGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    private sealed class PublishOrderWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "powerforge-publish-order", Guid.NewGuid().ToString("N"));

        internal DotNetRepositoryProjectResult AddProject(
            string name,
            string[]? dependencies = null,
            string? packageId = null,
            bool createNuspec = true,
            string version = "1.0.0",
            string dependencyVersion = "[1.0.0]",
            IReadOnlyDictionary<string, string[]>? dependencyGroups = null)
        {
            dependencies ??= dependencyGroups?.Values.SelectMany(value => value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
            packageId ??= name;
            var directory = Path.Combine(_root, name);
            Directory.CreateDirectory(directory);
            var references = string.Join(
                Environment.NewLine,
                dependencies.Select(dependency => $"    <ProjectReference Include=\"..{Path.DirectorySeparatorChar}{dependency}{Path.DirectorySeparatorChar}{dependency}.csproj\" />"));
            var itemGroup = dependencies.Length == 0
                ? string.Empty
                : $"  <ItemGroup>{Environment.NewLine}{references}{Environment.NewLine}  </ItemGroup>{Environment.NewLine}";
            var path = Path.Combine(directory, name + ".csproj");
            File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\">{Environment.NewLine}{itemGroup}</Project>");
            var packagePath = Path.Combine(directory, packageId + "." + version + ".nupkg");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                if (createNuspec)
                {
                    var entry = archive.CreateEntry(packageId + ".nuspec");
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    var groups = dependencyGroups ?? new Dictionary<string, string[]> { ["net8.0"] = dependencies };
                    var dependencyXml = string.Join(string.Empty, groups.Select(group =>
                        $"<group targetFramework=\"{group.Key}\">{string.Join(string.Empty, group.Value.Select(dependency => $"<dependency id=\"{dependency}\" version=\"{dependencyVersion}\" />"))}</group>"));
                    writer.Write($"<package><metadata><id>{packageId}</id><version>{version}</version><dependencies>{dependencyXml}</dependencies></metadata></package>");
                }
            }

            return new DotNetRepositoryProjectResult
            {
                ProjectName = name,
                PackageId = packageId,
                CsprojPath = path,
                NewVersion = version,
                Packages = [packagePath]
            };
        }

        internal void WriteRootFile(string relativePath, string content)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, relativePath), content);
        }

        internal void AddArchiveText(string archivePath, string entryPath, string content)
        {
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
            var entry = archive.CreateEntry(entryPath);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for test temp files.
            }
        }
    }
}
