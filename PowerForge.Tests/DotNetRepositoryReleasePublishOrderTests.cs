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
            File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\">{Environment.NewLine}{itemGroup}</Project>");
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
}
