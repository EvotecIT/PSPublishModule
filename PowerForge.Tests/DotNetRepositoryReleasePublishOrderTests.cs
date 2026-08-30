using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleasePublishOrderTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
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
    [Trait("Category", "DotNetPublishPrGate")]
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
    [Trait("Category", "DotNetPublishPrGate")]
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
    [Trait("Category", "DotNetPublishPrGate")]
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
    [Trait("Category", "DotNetPublishPrGate")]
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
    [Trait("Category", "DotNetPublishPrGate")]
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
    [Trait("Category", "DotNetPublishPrGate")]
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
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_UsesAuthoritativeMsBuildEvaluationForWhatIf()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(Configuration)' == 'Release'">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_WhatIfUsesPackTimeNoBuildProperty()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(NoBuild)' == 'true'">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_WhatIfMatchesPerProjectPackReferenceProperties()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(BuildProjectReferences)' != 'false'">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish(
                [app, shared],
                usePlannedProjectGraph: true,
                configuration: "Release",
                packStrategy: DotNetRepositoryPackStrategy.PerProject);

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_WhatIfUsesPerProjectFallbackWithoutMsBuildOutputPath()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(BuildProjectReferences)' != 'false'">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish(
                [app, shared],
                usePlannedProjectGraph: true,
                configuration: "Release",
                packStrategy: DotNetRepositoryPackStrategy.MSBuild,
                packageOutputPath: null);

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_WhatIfMatchesPackageOutputPath()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(PackageOutputPath)' != ''">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");
        var outputPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(app.CsprojPath)!)!, "packages");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish(
                [app, shared],
                usePlannedProjectGraph: true,
                configuration: "Release",
                packStrategy: DotNetRepositoryPackStrategy.MSBuild,
                packageOutputPath: outputPath);

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_WhatIfMatchesSymbolPackProperties()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup Condition="'$(IncludeSymbols)' == 'true' and '$(SymbolPackageFormat)' == 'snupkg'">
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish(
                [app, shared],
                usePlannedProjectGraph: true,
                configuration: "Release",
                includeSymbols: true);

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_WhatIfUsesInnerBuildsForMultiTargetProjects()
    {
        using var workspace = new PublishOrderWorkspace();
        var alpha = workspace.AddProject("Alpha");
        var beta = workspace.AddProject("Beta");
        File.WriteAllText(alpha.CsprojPath, MultiTargetProjectWithOuterReference("../Beta/Beta.csproj"));
        File.WriteAllText(beta.CsprojPath, MultiTargetProjectWithOuterReference("../Alpha/Alpha.csproj"));

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([beta, alpha], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Alpha", "Beta"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_WhatIfHonorsSuppressedPackageDependencies()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([shared, app], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["App", "Shared"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_WhatIfUsesCentralPackageVersionRange()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        shared.NewVersion = "2.0.0";
        var app = workspace.AddProject("App");
        var root = Path.GetDirectoryName(Path.GetDirectoryName(app.CsprojPath)!)!;
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), """
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
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_WhatIfKeepsPackageReferenceWithProjectOnlyMetadata()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Shared" Version="[1.0.0]" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
""");

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release");

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_UsesOnlyRootPackageManifest()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App", ["Shared"]);
        using (var archive = ZipFile.Open(app.Packages[0], ZipArchiveMode.Update))
        {
            var nested = archive.CreateEntry("tools/example.nuspec");
            using var writer = new StreamWriter(nested.Open(), new UTF8Encoding(false));
            writer.Write("<package><metadata><id>Example.Content</id></metadata></package>");
        }

        var ordered = new DotNetRepositoryReleaseService(new NullLogger())
            .SortProjectsForPublish([app, shared]);

        Assert.Equal(["Shared", "App"], ordered.Select(project => project.PackageId));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SortProjectsForPublish_FailsClosedForCustomNuspecWhatIf()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App");
        File.WriteAllText(app.CsprojPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <NuspecFile>package.nuspec</NuspecFile>
  </PropertyGroup>
</Project>
""");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DotNetRepositoryReleaseService(new NullLogger())
                .SortProjectsForPublish([app, shared], usePlannedProjectGraph: true, configuration: "Release"));

        Assert.Contains("intentionally unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("artifact-based ordering", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ExecuteNuGetPublishing_BlocksConsumersAfterDependencyFailure()
    {
        using var workspace = new PublishOrderWorkspace();
        var shared = workspace.AddProject("Shared");
        var app = workspace.AddProject("App", ["Shared"]);
        var independent = workspace.AddProject("Independent");
        var attempts = new List<string>();
        var root = Path.GetDirectoryName(Path.GetDirectoryName(shared.CsprojPath)!)!;
        var feed = Directory.CreateDirectory(Path.Combine(root, "feed"));
        var service = new DotNetRepositoryReleaseService(
            new NullLogger(),
            signPackages: null,
            getCertificateSha256: null,
            pushPackage: (packagePath, _, _, _, _, _) =>
            {
                var packageName = Path.GetFileName(packagePath);
                attempts.Add(packageName);
                return new DotNetRepositoryReleaseService.PackagePushResult
                {
                    Outcome = packageName.StartsWith("Shared.", StringComparison.OrdinalIgnoreCase)
                        ? DotNetRepositoryReleaseService.PackagePushOutcome.Failed
                        : DotNetRepositoryReleaseService.PackagePushOutcome.Published,
                    Message = "simulated push result"
                };
            });
        var result = new DotNetRepositoryReleaseResult();
        var spec = new DotNetRepositoryReleaseSpec
        {
            RootPath = root,
            Configuration = "Release",
            Publish = true,
            PublishApiKey = "test-key",
            PublishSource = feed.FullName,
            PublishFailFast = false,
            SkipDuplicate = true
        };

        var stopped = service.ExecuteNuGetPublishing(spec, result, [app, shared, independent], root, null, null);

        Assert.False(stopped);
        Assert.False(result.Success);
        Assert.Contains(attempts, package => package.StartsWith("Shared.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(attempts, package => package.StartsWith("Independent.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(attempts, package => package.StartsWith("App.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(app.Packages[0], result.FailedPackages, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PublishOrderWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "powerforge-publish-order", Guid.NewGuid().ToString("N"));

        internal DotNetRepositoryProjectResult AddProject(
            string name,
            string[]? dependencies = null,
            string? packageId = null,
            bool createNuspec = true)
        {
            dependencies ??= [];
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
            File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\">{Environment.NewLine}  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>{Environment.NewLine}{itemGroup}</Project>");
            var packagePath = Path.Combine(directory, packageId + ".1.0.0.nupkg");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                if (createNuspec)
                {
                    var entry = archive.CreateEntry(packageId + ".nuspec");
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    var dependencyXml = string.Join(string.Empty, dependencies.Select(dependency => $"<dependency id=\"{dependency}\" version=\"[1.0.0]\" />"));
                    writer.Write($"<package><metadata><id>{packageId}</id><version>1.0.0</version><dependencies><group targetFramework=\"net8.0\">{dependencyXml}</group></dependencies></metadata></package>");
                }
            }

            return new DotNetRepositoryProjectResult
            {
                ProjectName = name,
                PackageId = packageId,
                CsprojPath = path,
                NewVersion = "1.0.0",
                Packages = [packagePath]
            };
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

    private static string MultiTargetProjectWithOuterReference(string referencePath) => $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' == ''">
    <ProjectReference Include="{{referencePath}}" />
  </ItemGroup>
</Project>
""";
}
