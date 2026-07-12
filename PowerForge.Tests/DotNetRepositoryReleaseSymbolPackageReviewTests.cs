using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseSymbolPackageReviewTests
{
    [Theory]
    [InlineData(DotNetRepositoryPackStrategy.PerProject)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild)]
    public void Execute_DoesNotCollectProjectConfiguredSymbolsWithoutPowerForgeOptIn(
        DotNetRepositoryPackStrategy packStrategy)
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Package"));
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "Sample.Package.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <VersionPrefix>1.0.0</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "    <IncludeSymbols>true</IncludeSymbols>",
                "    <SymbolPackageFormat>snupkg</SymbolPackageFormat>",
                "  </PropertyGroup>",
                "</Project>"
            }));
            File.WriteAllText(
                Path.Combine(projectDirectory.FullName, "Sample.cs"),
                "namespace Sample.Package; public static class Sample { public static int Value => 1; }");

            var outputPath = Path.Combine(root.FullName, "packages");
            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = outputPath,
                Pack = true,
                PackStrategy = packStrategy,
                IncludeSymbols = false,
                Publish = false,
                UpdateVersions = false,
                CreateReleaseZip = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            var project = Assert.Single(result.Projects, item => item.IsPackable);
            Assert.Single(project.Packages);
            Assert.Empty(project.SymbolPackages);
            Assert.Single(Directory.EnumerateFiles(outputPath, "*.snupkg", SearchOption.AllDirectories));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
