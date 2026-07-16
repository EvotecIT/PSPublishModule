using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseCentralVersionTests
{
    [Fact]
    public void Execute_UpdateVersionsDisabled_UsesEvaluatedPackageVersionForMsBuildBatchCollection()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <ProductVersion>1.2.3</ProductVersion>
                  </PropertyGroup>
                </Project>
                """);

            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.CentralVersion"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.CentralVersion.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.CentralVersion</PackageId>
                    <VersionPrefix>$(ProductVersion)</VersionPrefix>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);

            var outputPath = Path.Combine(root.FullName, "packages");
            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = outputPath,
                Pack = true,
                PackStrategy = DotNetRepositoryPackStrategy.MSBuild,
                Publish = false,
                UpdateVersions = false,
                SignAssemblies = false,
                SignPackages = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("1.2.3", result.ResolvedVersionsByProject["Sample.CentralVersion"]);
            var project = Assert.Single(result.Projects, candidate => candidate.IsPackable);
            Assert.Equal("1.2.3", project.OldVersion);
            Assert.Equal("1.2.3", project.NewVersion);
            var package = Assert.Single(project.Packages);
            Assert.Equal("Sample.CentralVersion.1.2.3.nupkg", Path.GetFileName(package));
            Assert.True(File.Exists(package));
            Assert.DoesNotContain("$(ProductVersion)", result.ResolvedVersionsByProject.Values);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
