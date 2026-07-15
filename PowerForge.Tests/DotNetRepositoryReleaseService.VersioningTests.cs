using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseServiceVersioningTests
{
    [Fact]
    public void Execute_AlignsXPatternProjectsAfterHighestCurrentPackageVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WritePackableProject(root.FullName, "Suite.Core", "Suite.Core");
            WritePackableProject(root.FullName, "Suite.Rendering", "Suite.Renderer");
            WritePackableProject(root.FullName, "Suite.NewPackage", "Suite.NewPackage");

            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            File.WriteAllText(Path.Combine(source.FullName, "Suite.Core.2.0.2.nupkg"), string.Empty);
            File.WriteAllText(Path.Combine(source.FullName, "Suite.Renderer.2.0.5.nupkg"), string.Empty);

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Suite.Core"] = "2.0.X",
                    ["Suite.Rendering"] = "2.0.X",
                    ["Suite.NewPackage"] = "2.0.X"
                },
                ExpectedVersionMapAsInclude = true,
                AlignPackageVersions = true,
                VersionSources = new[] { source.FullName },
                UpdateVersions = true,
                Pack = false,
                WhatIf = true
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("2.0.6", result.ResolvedVersion);
            Assert.Equal("2.0.6", result.ResolvedVersionsByProject["Suite.Core"]);
            Assert.Equal("2.0.6", result.ResolvedVersionsByProject["Suite.Rendering"]);
            Assert.Equal("2.0.6", result.ResolvedVersionsByProject["Suite.NewPackage"]);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_KeepsExactVersionsOutsideXPatternAlignment()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WritePackableProject(root.FullName, "Suite.Core", "Suite.Core");
            WritePackableProject(root.FullName, "Suite.Fixed", "Suite.Fixed");

            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            File.WriteAllText(Path.Combine(source.FullName, "Suite.Core.2.0.5.nupkg"), string.Empty);

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Suite.Core"] = "2.0.X",
                    ["Suite.Fixed"] = "2.0.3"
                },
                ExpectedVersionMapAsInclude = true,
                AlignPackageVersions = true,
                VersionSources = new[] { source.FullName },
                UpdateVersions = true,
                Pack = false,
                WhatIf = true
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Null(result.ResolvedVersion);
            Assert.Equal("2.0.6", result.ResolvedVersionsByProject["Suite.Core"]);
            Assert.Equal("2.0.3", result.ResolvedVersionsByProject["Suite.Fixed"]);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_AcceptsExactPrereleasePackageVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Package"));
            var csprojPath = Path.Combine(projectDir.FullName, "Sample.Package.csproj");
            File.WriteAllText(csprojPath, string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <InformationalVersion>1.0.0+old</InformationalVersion>",
                "    <AssemblyVersion>2.0.0.0</AssemblyVersion>",
                "    <FileVersion>2.0.0.0</FileVersion>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            }));

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Sample.Package"] = "2.1.0-beta.1"
                },
                ExpectedVersionMapAsInclude = true,
                OutputPath = Path.Combine(root.FullName, "packages"),
                UpdateVersions = true,
                Pack = true,
                Publish = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.True(result.Success, result.ErrorMessage);
            var project = Assert.Single(result.Projects);
            Assert.Equal("2.1.0-beta.1", project.NewVersion);
            Assert.Contains(project.Packages, path => Path.GetFileName(path).Equals("Sample.Package.2.1.0-beta.1.nupkg", StringComparison.OrdinalIgnoreCase));
            var updated = File.ReadAllText(csprojPath);
            Assert.Contains("<Version>2.1.0-beta.1</Version>", updated, StringComparison.Ordinal);
            Assert.Contains("<InformationalVersion>2.1.0-beta.1</InformationalVersion>", updated, StringComparison.Ordinal);
            Assert.Contains("<AssemblyVersion>2.1.0</AssemblyVersion>", updated, StringComparison.Ordinal);
            Assert.Contains("<FileVersion>2.1.0</FileVersion>", updated, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("1.2.3+sha.abc", "1.2.3")]
    [InlineData("1.00.0-beta.1", "1.0.0-beta.1")]
    [InlineData("1.0.0.0-beta.1", "1.0.0-beta.1")]
    public void Execute_NormalizesExactVersionBeforePackageDiscovery(string requestedVersion, string normalizedVersion)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Package"));
            File.WriteAllText(Path.Combine(projectDir.FullName, "Sample.Package.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <Version>1.2.3</Version>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            }));

            var outputPath = Path.Combine(root.FullName, "packages");
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Sample.Package"] = requestedVersion
                },
                ExpectedVersionMapAsInclude = true,
                OutputPath = outputPath,
                UpdateVersions = true,
                Pack = true,
                Publish = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.True(result.Success, result.ErrorMessage);
            var project = Assert.Single(result.Projects);
            Assert.Equal(normalizedVersion, project.NewVersion);
            Assert.Contains(project.Packages, path => Path.GetFileName(path).Equals($"Sample.Package.{normalizedVersion}.nupkg", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_RejectsLeadingZeroNumericPrereleaseWithoutChangingProject()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Package"));
            var csprojPath = Path.Combine(projectDir.FullName, "Sample.Package.csproj");
            const string originalProject = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Version>1.0.0</Version><IsPackable>true</IsPackable></PropertyGroup></Project>";
            File.WriteAllText(csprojPath, originalProject);

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Sample.Package"] = "1.0.0-beta.01"
                },
                ExpectedVersionMapAsInclude = true,
                UpdateVersions = true,
                Pack = false,
                Publish = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.False(result.Success);
            Assert.Contains("Version resolution failed", Assert.Single(result.Projects).ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(originalProject, File.ReadAllText(csprojPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("2.1.0-beta.1", "2.1.0-beta.1", false)]
    [InlineData("2.1.0-beta.1", "2.1.0-beta.2", true)]
    [InlineData("2.1.0", "2.1.0-beta.2", false)]
    public void Execute_PublishPreflightComparesCompletePrereleaseVersions(string existingVersion, string requestedVersion, bool expectedSuccess)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Package"));
            File.WriteAllText(Path.Combine(projectDir.FullName, "Sample.Package.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <Version>1.0.0</Version>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            }));
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
            File.WriteAllText(Path.Combine(source.FullName, $"Sample.Package.{existingVersion}.nupkg"), "placeholder");

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Sample.Package"] = requestedVersion
                },
                ExpectedVersionMapAsInclude = true,
                OutputPath = Path.Combine(root.FullName, "packages"),
                VersionSources = new[] { source.FullName },
                PublishSource = source.FullName,
                PublishApiKey = "not-used-in-whatif",
                UpdateVersions = true,
                Pack = true,
                Publish = true,
                WhatIf = true,
                SkipDuplicate = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.Equal(expectedSuccess, result.Success);
            if (!expectedSuccess)
                Assert.Contains("already exists", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WritePackableProject(string rootPath, string projectName, string packageId)
    {
        var projectDirectory = Directory.CreateDirectory(Path.Combine(rootPath, projectName));
        File.WriteAllText(Path.Combine(projectDirectory.FullName, projectName + ".csproj"), string.Join(Environment.NewLine, new[]
        {
            "<Project Sdk=\"Microsoft.NET.Sdk\">",
            "  <PropertyGroup>",
            "    <TargetFramework>net8.0</TargetFramework>",
            $"    <PackageId>{packageId}</PackageId>",
            "    <Version>1.0.0</Version>",
            "    <IsPackable>true</IsPackable>",
            "  </PropertyGroup>",
            "</Project>"
        }));
    }
}
