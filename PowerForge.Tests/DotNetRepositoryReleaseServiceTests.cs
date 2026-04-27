using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseServiceTests
{
    [Fact]
    public void Execute_WhatIfPublish_DoesNotRequirePackageFilesOnDisk()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Src", "Sample.Package"));
            var csprojPath = Path.Combine(projectDir.FullName, "Sample.Package.csproj");
            File.WriteAllText(csprojPath, string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <VersionPrefix>1.2.3</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            }));

            var sourceDir = Directory.CreateDirectory(Path.Combine(root.FullName, "NugetSource"));

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "Artefacts", "packages"),
                Pack = true,
                Publish = true,
                WhatIf = true,
                PublishApiKey = "dummy",
                PublishSource = "https://api.nuget.org/v3/index.json",
                VersionSources = new[] { sourceDir.FullName },
                SkipDuplicate = true,
                UpdateVersions = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.True(result.Success);
            Assert.True(string.IsNullOrWhiteSpace(result.ErrorMessage), result.ErrorMessage);
            var project = Assert.Single(result.Projects, p => p.IsPackable);
            var pkg = Assert.Single(project.Packages);
            Assert.False(File.Exists(pkg));
            Assert.Contains(pkg, result.PublishedPackages, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_PublishPreflight_UsesPublishSourceWhenVersionSourcesAreMissing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Src", "Sample.Package"));
            var csprojPath = Path.Combine(projectDir.FullName, "Sample.Package.csproj");
            File.WriteAllText(csprojPath, string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <VersionPrefix>1.2.3</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            }));

            var publishSourceDir = Directory.CreateDirectory(Path.Combine(root.FullName, "PublishSource"));
            File.WriteAllText(Path.Combine(publishSourceDir.FullName, "Sample.Package.1.2.3.nupkg"), "placeholder");

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "Artefacts", "packages"),
                Pack = true,
                Publish = true,
                WhatIf = true,
                PublishApiKey = "dummy",
                PublishSource = publishSourceDir.FullName,
                SkipDuplicate = false,
                UpdateVersions = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.False(result.Success);
            Assert.Contains("already exists", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sample.Package version 1.2.3", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_IgnoresNestedGitWorktreeRoots_DuringProjectDiscovery()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Src", "Sample.Package"));
            File.WriteAllText(Path.Combine(projectDir.FullName, "Sample.Package.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <VersionPrefix>1.2.3</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            }));

            var nestedWorktreeRoot = Directory.CreateDirectory(Path.Combine(root.FullName, ".claude", "worktrees", "sample-review"));
            File.WriteAllText(Path.Combine(nestedWorktreeRoot.FullName, ".git"), "gitdir: C:/Support/GitHub/PSPublishModule/.git/worktrees/sample-review");

            var nestedProjectDir = Directory.CreateDirectory(Path.Combine(nestedWorktreeRoot.FullName, "Src", "Sample.Package"));
            File.WriteAllText(Path.Combine(nestedProjectDir.FullName, "Sample.Package.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <VersionPrefix>9.9.9</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            }));

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                Pack = false,
                Publish = false,
                UpdateVersions = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.True(result.Success, result.ErrorMessage);
            var project = Assert.Single(result.Projects);
            Assert.Equal("Sample.Package", project.ProjectName);
            Assert.Equal("1.2.3", project.NewVersion);
            Assert.DoesNotContain("Duplicate project name", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ClassifyNuGetPushOutcome_ReturnsSkippedDuplicate_WhenDotNetReportsExistingPackage()
    {
        var result = DotNetRepositoryReleaseService.ClassifyNuGetPushOutcome(
            exitCode: 0,
            skipDuplicate: true,
            stdErr: string.Empty,
            stdOut: "Package 'DbaClientX.SqlServer.0.1.0.nupkg' already exists and cannot be modified. The server returned 409 (Conflict).");

        Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate, result.Outcome);
    }

    [Fact]
    public void ClassifyNuGetPushOutcome_ReturnsPublished_WhenDotNetReportsSuccessfulUpload()
    {
        var result = DotNetRepositoryReleaseService.ClassifyNuGetPushOutcome(
            exitCode: 0,
            skipDuplicate: true,
            stdErr: string.Empty,
            stdOut: "Your package was pushed.");

        Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.Published, result.Outcome);
    }

    [Fact]
    public void WritePackTraversalProject_EmitsBatchPackProjectWithEscapedProperties()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Src", "Sample.Package"));
            var csprojPath = Path.Combine(projectDir.FullName, "Sample.Package.csproj");
            var traversalPath = Path.Combine(root.FullName, "pack.proj");
            var outputPath = Path.Combine(root.FullName, "packages;with%value");
            var project = new DotNetRepositoryProjectResult
            {
                ProjectName = "Sample.Package",
                CsprojPath = csprojPath,
                PackageId = "Sample.Package",
                IsPackable = true
            };
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release"
            };

            DotNetRepositoryReleaseService.WritePackTraversalProject(
                traversalPath,
                new[] { project },
                spec,
                outputPath);

            var document = XDocument.Load(traversalPath);
            var packProject = Assert.Single(document.Descendants("PackProject"));
            Assert.Equal(Path.GetFullPath(csprojPath), packProject.Attribute("Include")?.Value);

            var msbuild = Assert.Single(document.Descendants("MSBuild"));
            Assert.Equal("@(PackProject)", msbuild.Attribute("Projects")?.Value);
            Assert.Equal("Restore;Pack", msbuild.Attribute("Targets")?.Value);
            Assert.Equal("true", msbuild.Attribute("BuildInParallel")?.Value);
            Assert.Equal(
                $"Configuration=Release;PackageOutputPath={outputPath.Replace("%", "%25").Replace(";", "%3B")}",
                msbuild.Attribute("Properties")?.Value);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(5_400_000, "1h 30.0m")]
    [InlineData(65_000, "1.1m")]
    [InlineData(1_500, "1.5s")]
    [InlineData(12, "12ms")]
    public void FormatDuration_returns_human_readable_output(int milliseconds, string expected)
    {
        var formatted = DotNetRepositoryReleaseService.FormatDuration(TimeSpan.FromMilliseconds(milliseconds));

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void FormatBytes_returns_human_readable_output(long bytes, string expected)
    {
        Assert.Equal(expected, DotNetRepositoryReleaseService.FormatBytes(bytes));
    }

    [Fact]
    public void PackageSnapshot_DetectsNewAndChangedPackages()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var existing = Path.Combine(root.FullName, "Sample.Package.1.0.0.nupkg");
            var symbols = Path.Combine(root.FullName, "Sample.Package.1.0.0.symbols.nupkg");
            File.WriteAllText(existing, "old");
            File.WriteAllText(symbols, "symbols");

            var snapshot = DotNetRepositoryReleaseService.SnapshotPackages(root.FullName);

            Assert.False(DotNetRepositoryReleaseService.WasPackageCreatedOrChanged(snapshot, existing));

            File.WriteAllText(existing, "changed package");
            var created = Path.Combine(root.FullName, "Sample.Package.1.0.1.nupkg");
            File.WriteAllText(created, "new");

            Assert.True(DotNetRepositoryReleaseService.WasPackageCreatedOrChanged(snapshot, existing));
            Assert.True(DotNetRepositoryReleaseService.WasPackageCreatedOrChanged(snapshot, created));

            File.Delete(existing);
            Assert.False(DotNetRepositoryReleaseService.WasPackageCreatedOrChanged(snapshot, existing));
            Assert.DoesNotContain(symbols, snapshot.Keys, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
