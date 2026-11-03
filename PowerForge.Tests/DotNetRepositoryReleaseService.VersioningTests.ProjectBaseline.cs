using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using NuGet.Versioning;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleaseServiceVersioningTests
{
    [Fact]
    public void Execute_PacksResolvedInheritedVersionsWithoutChangingProjectReferenceVersions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"),
                """
                <Project><PropertyGroup>
                  <PackageVersion>2.0.5</PackageVersion>
                  <PackageVersion Condition="'$(MSBuildProjectName)' == 'Suite.Dependency'">3.0.8</PackageVersion>
                </PropertyGroup></Project>
                """);
            WritePackableProject(root.FullName, "Suite.Core", "Suite.Core");
            WritePackableProject(root.FullName, "Suite.Dependency", "Suite.Dependency");
            var projectPath = Path.Combine(root.FullName, "Suite.Core", "Suite.Core.csproj");
            File.WriteAllText(projectPath, File.ReadAllText(projectPath).Replace("</Project>",
                "<ItemGroup><ProjectReference Include=\"../Suite.Dependency/Suite.Dependency.csproj\" /></ItemGroup></Project>"));
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Suite.Core"] = "2.0.X",
                    ["Suite.Dependency"] = "3.0.X"
                },
                VersionSources = new[] { source.FullName },
                OutputPath = Path.Combine(root.FullName, "packages"),
                UpdateVersions = true,
                Pack = true,
                Publish = false
            });

            Assert.True(result.Success, result.ErrorMessage ?? string.Join("; ", result.Projects.Select(project => project.ErrorMessage)));
            var project = Assert.Single(result.Projects, item => item.ProjectName == "Suite.Core");
            Assert.Equal("2.0.6", project.NewVersion);
            using var package = ZipFile.OpenRead(Assert.Single(project.Packages));
            using var manifest = Assert.Single(package.Entries, entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            var metadata = XDocument.Load(manifest);
            Assert.Equal("2.0.6", Assert.Single(metadata.Descendants(), element => element.Name.LocalName == "version").Value);
            var dependency = Assert.Single(metadata.Descendants(), element =>
                element.Name.LocalName == "dependency" && (string?)element.Attribute("id") == "Suite.Dependency");
            Assert.Equal("3.0.9", VersionRange.Parse((string)dependency.Attribute("version")!).MinVersion!.ToNormalizedString());
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false, false, "2.0.6", "2.0.4")]
    [InlineData(true, false, "2.0.6", "2.0.6")]
    [InlineData(false, true, "2.0.3", "2.0.3")]
    [InlineData(true, true, "2.0.3", "2.0.3")]
    public void Execute_UsesEvaluatedProjectBaselineOnlyWhenNoPackageExists(
        bool alignPackages, bool hasPublishedPackage, string expectedCoreVersion, string expectedRenderingVersion)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"),
                """
                <Project><PropertyGroup>
                  <PackageVersion>2.0.5</PackageVersion>
                  <PackageVersion Condition="'$(MSBuildProjectName)' == 'Suite.Rendering'">2.0.3</PackageVersion>
                </PropertyGroup></Project>
                """);
            WritePackableProject(root.FullName, "Suite.Core", "Suite.Core");
            WritePackableProject(root.FullName, "Suite.Rendering", "Suite.Renderer");
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            if (hasPublishedPackage)
            {
                File.WriteAllText(Path.Combine(source.FullName, "Suite.Core.2.0.2.nupkg"), string.Empty);
                File.WriteAllText(Path.Combine(source.FullName, "Suite.Renderer.2.0.2.nupkg"), string.Empty);
            }

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersion = "2.0.X",
                AlignPackageVersions = alignPackages,
                VersionSources = new[] { source.FullName },
                UpdateVersions = true,
                Pack = false,
                WhatIf = true
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, result.ResolvedVersionsByProject.Count);
            Assert.Equal(expectedCoreVersion, result.ResolvedVersionsByProject["Suite.Core"]);
            Assert.Equal(expectedRenderingVersion, result.ResolvedVersionsByProject["Suite.Rendering"]);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
