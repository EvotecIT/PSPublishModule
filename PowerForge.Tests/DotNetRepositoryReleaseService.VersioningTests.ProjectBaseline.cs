using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetRepositoryReleaseServiceVersioningTests
{
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
