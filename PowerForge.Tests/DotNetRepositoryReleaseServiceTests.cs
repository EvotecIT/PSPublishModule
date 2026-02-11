using System;
using System.IO;
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
}
