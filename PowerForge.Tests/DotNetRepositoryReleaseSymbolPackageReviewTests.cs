using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseSymbolPackageReviewTests
{
    [Fact]
    public void ClassifyNuGetPushOutcome_DoesNotTreatLocalSkipDuplicateWarningAsSkipped()
    {
        var result = DotNetRepositoryReleaseService.ClassifyNuGetPushOutcome(
            exitCode: 0,
            skipDuplicate: true,
            stdErr: "The option to skip duplicates is not currently supported for this type of push.",
            stdOut: "Your package was pushed.");

        Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.Published, result.Outcome);
    }

    [Fact]
    public void ClassifyPublishedArtifacts_PreservesPublishedPrimaryWhenCompanionFails()
    {
        var primary = "Sample.1.0.0.nupkg";
        var symbols = "Sample.1.0.0.snupkg";
        var pushResult = new DotNetRepositoryReleaseService.PackagePushResult
        {
            Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Failed,
            Message = string.Join(Environment.NewLine, new[]
            {
                $"Pushing {primary}...",
                "Your package was pushed.",
                $"Pushing {symbols}...",
                "Response status code does not indicate success: 400 (The symbol package is invalid)."
            })
        };

        var outcomes = DotNetRepositoryReleaseService.ClassifyPublishedArtifacts(
            new[] { primary, symbols },
            pushResult,
            skipDuplicate: true);

        Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.Published, outcomes[primary]);
        Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.Failed, outcomes[symbols]);
    }

    [Fact]
    public void ClassifyPublishedArtifacts_PreservesConflictFailureWhenSkipDuplicateIsDisabled()
    {
        var primary = "Sample.1.0.0.nupkg";
        var symbols = "Sample.1.0.0.snupkg";
        var pushResult = new DotNetRepositoryReleaseService.PackagePushResult
        {
            Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Failed,
            Message = string.Join(Environment.NewLine, new[]
            {
                $"Pushing {primary}...",
                "Your package was pushed.",
                $"Pushing {symbols}...",
                $"Package '{symbols}' already exists and cannot be modified. The server returned 409 (Conflict)."
            })
        };

        var outcomes = DotNetRepositoryReleaseService.ClassifyPublishedArtifacts(
            new[] { primary, symbols },
            pushResult,
            skipDuplicate: false);

        Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.Published, outcomes[primary]);
        Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.Failed, outcomes[symbols]);
    }

    [Fact]
    public void GetPackagesForPublish_IncludesSymbolsWhenRequestedForLocalFeed()
    {
        var project = new DotNetRepositoryProjectResult { ProjectName = "Sample" };
        project.Packages.Add("Sample.1.0.0.nupkg");
        project.SymbolPackages.Add("Sample.1.0.0.snupkg");

        var packages = DotNetRepositoryReleaseService.GetPackagesForPublish(
            new[] { project },
            includeSymbolPackages: true);

        Assert.Equal(new[]
        {
            "Sample.1.0.0.nupkg",
            "Sample.1.0.0.snupkg"
        }, packages);
    }

    [Fact]
    public void PushPackage_CopiesExplicitSymbolPackageIntoLocalFeed()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var packageDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "packages"));
            var localFeed = Directory.CreateDirectory(Path.Combine(root.FullName, "feed"));
            var primary = Path.Combine(localFeed.FullName, "Sample.1.0.0.nupkg");
            var symbols = Path.Combine(packageDirectory.FullName, "Sample.1.0.0.snupkg");
            File.WriteAllText(primary, "primary");
            File.WriteAllText(symbols, "symbols");

            var result = DotNetRepositoryReleaseService.PushPackage(
                symbols,
                "unused-for-local-feed",
                new Uri(localFeed.FullName).AbsoluteUri,
                skipDuplicate: true,
                suppressCompanionSymbols: true);

            Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.Published, result.Outcome);
            Assert.True(File.Exists(Path.Combine(localFeed.FullName, Path.GetFileName(symbols))));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_WithSymbolsAndNamedLocalFeed_PublishesBothArtifacts()
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
            File.WriteAllText(
                Path.Combine(projectDir.FullName, "Class1.cs"),
                "namespace Sample.Package; public static class Class1 { public static string Value => \"symbols\"; }");

            var localFeed = Directory.CreateDirectory(Path.Combine(root.FullName, "feed"));
            File.WriteAllText(
                Path.Combine(root.FullName, "NuGet.config"),
                $"<configuration><packageSources><clear /><add key=\"LocalFeed\" value=\"{localFeed.FullName}\" /></packageSources></configuration>");
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "packages"),
                Pack = true,
                IncludeSymbols = true,
                Publish = true,
                PublishApiKey = "unused-for-local-feed",
                PublishSource = "LocalFeed",
                SkipDuplicate = true,
                UpdateVersions = false,
                CreateReleaseZip = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.True(result.Success, result.ErrorMessage);
            var project = Assert.Single(result.Projects, item => item.IsPackable);
            var primary = Assert.Single(project.Packages);
            var symbols = Assert.Single(project.SymbolPackages);
            Assert.True(
                result.PublishedPackages.Contains(primary, StringComparer.OrdinalIgnoreCase),
                $"Primary package was not reported as published. Expected: {primary}. Published: {string.Join(", ", result.PublishedPackages)}. Skipped: {string.Join(", ", result.SkippedDuplicatePackages)}. Failed: {string.Join(", ", result.FailedPackages)}");
            Assert.True(
                result.PublishedPackages.Contains(symbols, StringComparer.OrdinalIgnoreCase),
                $"Symbol package was not reported as published. Expected: {symbols}. Actual: {string.Join(", ", result.PublishedPackages)}");
            Assert.True(File.Exists(Path.Combine(localFeed.FullName, Path.GetFileName(primary))));
            Assert.True(File.Exists(Path.Combine(localFeed.FullName, Path.GetFileName(symbols))));
            Assert.Empty(result.FailedPackages);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

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
