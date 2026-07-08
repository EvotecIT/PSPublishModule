using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseServiceTests
{
    [Fact]
    public void Execute_preserves_one_argument_public_overload()
    {
        var overload = typeof(DotNetRepositoryReleaseService)
            .GetMethod(nameof(DotNetRepositoryReleaseService.Execute), new[] { typeof(DotNetRepositoryReleaseSpec) });

        Assert.NotNull(overload);
    }

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
            var outputPath = Path.Combine(root.FullName, "packages;with%value=2$build@local");
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

            var msbuildTasks = document.Descendants("MSBuild").ToArray();
            Assert.Equal(3, msbuildTasks.Length);
            Assert.All(msbuildTasks, msbuild =>
            {
                Assert.Equal("@(PackProject)", msbuild.Attribute("Projects")?.Value);
                Assert.Equal("true", msbuild.Attribute("BuildInParallel")?.Value);
                Assert.Equal("true", msbuild.Attribute("StopOnFirstFailure")?.Value);
            });
            Assert.Equal("Restore", msbuildTasks[0].Attribute("Targets")?.Value);
            Assert.Equal("Build", msbuildTasks[1].Attribute("Targets")?.Value);
            Assert.Equal("Pack", msbuildTasks[2].Attribute("Targets")?.Value);
            Assert.Equal("Configuration=Release", msbuildTasks[0].Attribute("Properties")?.Value);
            Assert.Equal("Configuration=Release", msbuildTasks[1].Attribute("Properties")?.Value);
            Assert.Equal("RestoreSelected", msbuildTasks[0].Parent?.Attribute("Name")?.Value);
            Assert.Equal("BuildSelected", msbuildTasks[1].Parent?.Attribute("Name")?.Value);
            Assert.Equal("PackOnlySelected", msbuildTasks[2].Parent?.Attribute("Name")?.Value);
            Assert.Equal("RestoreSelected", msbuildTasks[1].Parent?.Attribute("DependsOnTargets")?.Value);
            Assert.Equal("BuildSelected;PackOnlySelected", document.Descendants("Target")
                .Single(target => string.Equals(target.Attribute("Name")?.Value, "PackSelected", StringComparison.Ordinal))
                .Attribute("DependsOnTargets")?.Value);
            Assert.Equal(
                $"Configuration=Release;PackageOutputPath={outputPath.Replace("%", "%25").Replace(";", "%3B").Replace("=", "%3D").Replace("$", "%24").Replace("@", "%40")};NoBuild=true;BuildProjectReferences=false",
                msbuildTasks[2].Attribute("Properties")?.Value);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_MsBuildBatchPack_PacksProjectReferenceGraphOnce()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var sharedDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Shared"));
            File.WriteAllText(Path.Combine(sharedDir.FullName, "Shared.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Shared</PackageId>",
                "    <VersionPrefix>1.0.0</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            }));
            File.WriteAllText(Path.Combine(sharedDir.FullName, "Greeter.cs"), "namespace Sample.Shared; public static class Greeter { public static string Hello() => \"hello\"; }");

            var consumerDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Consumer"));
            File.WriteAllText(Path.Combine(consumerDir.FullName, "Consumer.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Consumer</PackageId>",
                "    <VersionPrefix>1.0.0</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "  <ItemGroup>",
                "    <ProjectReference Include=\"..\\Shared\\Shared.csproj\" />",
                "  </ItemGroup>",
                "</Project>"
            }));
            File.WriteAllText(Path.Combine(consumerDir.FullName, "Consumer.cs"), "namespace Sample.Consumer; public static class Consumer { public static string Hello() => Sample.Shared.Greeter.Hello(); }");

            var outputPath = Path.Combine(root.FullName, "packages");
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = outputPath,
                Pack = true,
                PackStrategy = DotNetRepositoryPackStrategy.MSBuild,
                Publish = false,
                UpdateVersions = false,
                CreateReleaseZip = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(File.Exists(Path.Combine(outputPath, "Sample.Shared.1.0.0.nupkg")));
            Assert.True(File.Exists(Path.Combine(outputPath, "Sample.Consumer.1.0.0.nupkg")));
            Assert.All(result.Projects.Where(project => project.IsPackable), project =>
            {
                Assert.Single(project.Packages);
                Assert.True(string.IsNullOrWhiteSpace(project.ErrorMessage), project.ErrorMessage);
            });
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_MsBuildBatchPack_WithAssemblySigning_SignsBuildOutputsBeforeNoBuildPack()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var sharedDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Shared"));
            File.WriteAllText(Path.Combine(sharedDir.FullName, "Shared.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Shared</PackageId>",
                "    <VersionPrefix>1.0.0</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            }));
            File.WriteAllText(Path.Combine(sharedDir.FullName, "Greeter.cs"), "namespace Sample.Shared; public static class Greeter { public static string Hello() => \"hello\"; }");

            var consumerDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Consumer"));
            File.WriteAllText(Path.Combine(consumerDir.FullName, "Consumer.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Consumer</PackageId>",
                "    <VersionPrefix>1.0.0</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "  <ItemGroup>",
                "    <ProjectReference Include=\"..\\Shared\\Shared.csproj\" />",
                "  </ItemGroup>",
                "</Project>"
            }));
            File.WriteAllText(Path.Combine(consumerDir.FullName, "Consumer.cs"), "namespace Sample.Consumer; public static class Consumer { public static string Hello() => Sample.Shared.Greeter.Hello(); }");

            var signedAssemblies = 0;
            var outputPath = Path.Combine(root.FullName, "packages");
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = outputPath,
                Pack = true,
                PackStrategy = DotNetRepositoryPackStrategy.MSBuild,
                Publish = false,
                UpdateVersions = false,
                CreateReleaseZip = false,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignPackages = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                spec,
                request =>
                {
                    Assert.Equal("ABC123", request.CertificateThumbprint);
                    var filePaths = Assert.IsType<string[]>(request.FilePaths);
                    signedAssemblies += filePaths.Count(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(filePaths, path => path.EndsWith(Path.Combine("Shared", "bin", "Release", "net8.0", "Shared.dll"), StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(filePaths, path => path.EndsWith(Path.Combine("Consumer", "bin", "Release", "net8.0", "Consumer.dll"), StringComparison.OrdinalIgnoreCase));
                },
                _ => { });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, signedAssemblies);
            Assert.True(File.Exists(Path.Combine(outputPath, "Sample.Shared.1.0.0.nupkg")));
            Assert.True(File.Exists(Path.Combine(outputPath, "Sample.Consumer.1.0.0.nupkg")));
            Assert.All(result.Projects.Where(project => project.IsPackable), project =>
            {
                Assert.Single(project.Packages);
                Assert.True(string.IsNullOrWhiteSpace(project.ErrorMessage), project.ErrorMessage);
            });
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_WithAssemblySigning_SignsBuildOutputsBeforePacking()
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
            File.WriteAllText(Path.Combine(projectDir.FullName, "Class1.cs"), "namespace Sample.Package; public static class Class1 { public static string Value => \"signed\"; }");

            var signedMarker = string.Empty;
            var signingCalls = 0;
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "packages"),
                ReleaseZipOutputPath = Path.Combine(root.FullName, "releases"),
                Pack = true,
                Publish = false,
                UpdateVersions = false,
                CreateReleaseZip = true,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignPackages = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                spec,
                request =>
                {
                    signingCalls++;
                    Assert.Equal(CertificateStoreLocation.CurrentUser, request.LocalStore);
                    Assert.Equal("ABC123", request.CertificateThumbprint);
                    Assert.Contains("Sample.Package.dll", request.IncludePatterns);
                    Assert.Contains("Sample.Package.exe", request.IncludePatterns);
                    Assert.DoesNotContain("*.dll", request.IncludePatterns);
                    var filePaths = Assert.IsType<string[]>(request.FilePaths);
                    var assembly = filePaths.SingleOrDefault(path => path.EndsWith("Sample.Package.dll", StringComparison.OrdinalIgnoreCase));
                    Assert.False(string.IsNullOrWhiteSpace(assembly));
                    signedMarker = Path.Combine(Path.GetDirectoryName(assembly!)!, "signed.marker");
                    File.WriteAllText(signedMarker, "signed-before-pack");
                },
                request =>
                {
                    Assert.Equal(CertificateStoreLocation.CurrentUser, request.LocalStore);
                    Assert.Equal("ABC123", request.CertificateThumbprint);
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, signingCalls);
            Assert.True(File.Exists(signedMarker));
            var project = Assert.Single(result.Projects, item => item.IsPackable);
            Assert.Single(project.Packages);
            Assert.True(File.Exists(project.Packages[0]));
            Assert.True(File.Exists(project.ReleaseZipPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_WithPackageSigningFailure_DoesNotPublishUnsignedPackagesWhenFailFastIsDisabled()
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
            File.WriteAllText(Path.Combine(projectDir.FullName, "Class1.cs"), "namespace Sample.Package; public static class Class1 { public static string Value => \"signed\"; }");

            var localFeed = Directory.CreateDirectory(Path.Combine(root.FullName, "feed"));
            var packagesSeenBySigner = Array.Empty<string>();
            DotNetRepositoryReleaseService.PackageSigningHandler signPackages = (
                IReadOnlyList<string> packages,
                DotNetRepositoryReleaseSpec spec,
                string sha256,
                out string error) =>
            {
                packagesSeenBySigner = packages.ToArray();
                error = "simulated signing failure";
                return false;
            };

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "packages"),
                Pack = true,
                Publish = true,
                PublishApiKey = "key",
                PublishSource = localFeed.FullName,
                PublishFailFast = false,
                SkipDuplicate = true,
                UpdateVersions = false,
                CreateReleaseZip = false,
                CertificateThumbprint = "ABC123",
                SignAssemblies = false,
                SignPackages = true
            };

            var result = new DotNetRepositoryReleaseService(
                new NullLogger(),
                signPackages,
                (_, _) => "ABCDEF").Execute(spec);

            Assert.False(result.Success);
            Assert.Single(packagesSeenBySigner);
            Assert.Empty(result.PublishedPackages);
            Assert.Empty(Directory.EnumerateFiles(localFeed.FullName, "*.nupkg", SearchOption.AllDirectories));
            var project = Assert.Single(result.Projects, item => item.IsPackable);
            Assert.Contains("Package signing failed", project.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Publish preflight failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_WithAssemblySigning_UsesEvaluatedAssemblyNameWhenImported()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), string.Join(Environment.NewLine, new[]
            {
                "<Project>",
                "  <PropertyGroup>",
                "    <AssemblyName>Signed.Sample</AssemblyName>",
                "  </PropertyGroup>",
                "</Project>"
            }));

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
            File.WriteAllText(Path.Combine(projectDir.FullName, "Class1.cs"), "namespace Sample.Package; public static class Class1 { public static string Value => \"signed\"; }");

            var signedPath = string.Empty;
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "packages"),
                Pack = true,
                Publish = false,
                UpdateVersions = false,
                CreateReleaseZip = false,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignPackages = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                spec,
                request =>
                {
                    Assert.Contains("Signed.Sample.dll", request.IncludePatterns);
                    Assert.DoesNotContain("*.dll", request.IncludePatterns);
                    var filePaths = Assert.IsType<string[]>(request.FilePaths);
                    signedPath = filePaths.Single(path => path.EndsWith("Signed.Sample.dll", StringComparison.OrdinalIgnoreCase));
                },
                _ => { });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(string.IsNullOrWhiteSpace(signedPath));
            Assert.True(File.Exists(signedPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_WithAssemblySigning_UsesEvaluatedAssemblyNameWhenConditional()
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
                "  <PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">",
                "    <AssemblyName>Debug.Sample</AssemblyName>",
                "  </PropertyGroup>",
                "  <PropertyGroup Condition=\"'$(Configuration)' == 'Release'\">",
                "    <AssemblyName>Release.Sample</AssemblyName>",
                "  </PropertyGroup>",
                "</Project>"
            }));
            File.WriteAllText(Path.Combine(projectDir.FullName, "Class1.cs"), "namespace Sample.Package; public static class Class1 { public static string Value => \"signed\"; }");

            var signedPath = string.Empty;
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "packages"),
                Pack = true,
                Publish = false,
                UpdateVersions = false,
                CreateReleaseZip = false,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignPackages = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                spec,
                request =>
                {
                    Assert.Contains("Release.Sample.dll", request.IncludePatterns);
                    Assert.DoesNotContain("Debug.Sample.dll", request.IncludePatterns);
                    var filePaths = Assert.IsType<string[]>(request.FilePaths);
                    signedPath = filePaths.Single(path => path.EndsWith("Release.Sample.dll", StringComparison.OrdinalIgnoreCase));
                },
                _ => { });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(string.IsNullOrWhiteSpace(signedPath));
            Assert.True(File.Exists(signedPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_WithAssemblySigningDependencyOptIn_UsesBroadIncludePatterns()
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
            File.WriteAllText(Path.Combine(projectDir.FullName, "Class1.cs"), "namespace Sample.Package; public static class Class1 { public static string Value => \"signed\"; }");

            string[]? includePatterns = null;
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "packages"),
                Pack = true,
                Publish = false,
                UpdateVersions = false,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignDependencyAssemblies = true,
                SignPackages = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                spec,
                request => includePatterns = request.IncludePatterns,
                _ => { });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(includePatterns);
            Assert.Contains("*.dll", includePatterns!);
            Assert.Contains("*.exe", includePatterns!);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_WithAssemblySigning_UsesCustomBuildOutputDirectory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Src", "Sample.Package"));
            var customOutput = Path.Combine(root.FullName, "custom-output");
            File.WriteAllText(Path.Combine(projectDir.FullName, "Sample.Package.csproj"), string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <VersionPrefix>1.2.3</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                $"    <OutputPath>{customOutput}</OutputPath>",
                "  </PropertyGroup>",
                "</Project>"
            }));
            File.WriteAllText(Path.Combine(projectDir.FullName, "Class1.cs"), "namespace Sample.Package; public static class Class1 { public static string Value => \"custom\"; }");

            var signedPath = string.Empty;
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "packages"),
                Pack = true,
                Publish = false,
                UpdateVersions = false,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignPackages = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                spec,
                request =>
                {
                    var filePaths = Assert.IsType<string[]>(request.FilePaths);
                    var assembly = filePaths.SingleOrDefault(path => path.EndsWith("Sample.Package.dll", StringComparison.OrdinalIgnoreCase));
                    Assert.False(string.IsNullOrWhiteSpace(assembly));
                    Assert.Contains("custom-output", assembly, StringComparison.OrdinalIgnoreCase);
                    signedPath = assembly!;
                },
                _ => { });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(File.Exists(signedPath));
            var project = Assert.Single(result.Projects, item => item.IsPackable);
            Assert.Single(project.Packages);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveBuildOutputDirectories_ProbesEvaluatedTargetDirForMissingTargetFrameworkOutputs()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Src", "Sample.Package"));
            var customNet10Output = Path.Combine(root.FullName, "custom-net10");
            var customNet10TargetDir = Path.Combine(customNet10Output, "net10.0");
            var customNet10OutputPath = customNet10Output + Path.DirectorySeparatorChar;
            var csprojPath = Path.Combine(projectDir.FullName, "Sample.Package.csproj");
            File.WriteAllText(csprojPath, string.Join(Environment.NewLine, new[]
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>",
                "    <PackageId>Sample.Package</PackageId>",
                "    <VersionPrefix>1.2.3</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "  <PropertyGroup Condition=\"'$(TargetFramework)' == 'net10.0'\">",
                $"    <OutputPath>{customNet10OutputPath}</OutputPath>",
                "  </PropertyGroup>",
                "</Project>"
            }));
            var conventionalNet8Output = Directory.CreateDirectory(Path.Combine(projectDir.FullName, "bin", "Release", "net8.0"));
            Directory.CreateDirectory(customNet10TargetDir);
            File.WriteAllText(Path.Combine(conventionalNet8Output.FullName, "Sample.Package.dll"), "net8");
            File.WriteAllText(Path.Combine(customNet10TargetDir, "Sample.Package.dll"), "net10");

            var method = typeof(DotNetRepositoryReleaseService).GetMethod(
                "ResolveBuildOutputDirectories",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var directories = Assert.IsType<string[]>(method!.Invoke(null, new object?[]
            {
                csprojPath,
                projectDir.FullName,
                "Release",
                "Sample.Package",
                new NullLogger(),
                new[] { "Sample.Package.dll" }
            }));

            Assert.Contains(directories, path => path.EndsWith(Path.Combine("bin", "Release", "net8.0"), StringComparison.OrdinalIgnoreCase));
            var expectedCustomOutput = Path.GetFullPath(customNet10TargetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Assert.Contains(directories, path => string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                expectedCustomOutput,
                StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_WithAssemblySigning_PreflightsBeforeEditingProjectVersion()
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

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersion = "1.2.4",
                Pack = true,
                Publish = false,
                UpdateVersions = true,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignPackages = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                spec,
                _ => throw new InvalidOperationException("Signing should not run."),
                _ => throw new InvalidOperationException("missing signing prerequisites"));

            Assert.False(result.Success);
            Assert.Contains("preflight", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<VersionPrefix>1.2.3</VersionPrefix>", File.ReadAllText(csprojPath), StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_FailsClearlyWhenAssemblySigningIsRequestedWithoutHandler()
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

            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Pack = true,
                Publish = false,
                UpdateVersions = false,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignPackages = false
            };

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(spec);

            Assert.False(result.Success);
            Assert.Contains("assembly signing handler", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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
    public void SummarizeProcessOutputLines_includes_first_last_and_diagnostic_lines()
    {
        var lines = Enumerable.Range(1, 80)
            .Select(i => i == 31 ? "Project.csproj : error NU1301: Unable to load service index" : $"line {i}")
            .ToArray();

        var summary = DotNetRepositoryReleaseService.SummarizeProcessOutputLines(string.Join(Environment.NewLine, lines));

        Assert.Contains("line 1", summary);
        Assert.Contains("... omitted 30 line(s); diagnostic lines from that range are shown below when detected ...", summary);
        Assert.Contains("diagnostic lines:", summary);
        Assert.Contains("Project.csproj : error NU1301: Unable to load service index", summary);
        Assert.Contains("last 40 line(s):", summary);
        Assert.Contains("line 80", summary);
    }

    [Fact]
    public void SummarizeProcessOutputLines_returns_short_output_verbatim()
    {
        var output = string.Join(Environment.NewLine, new[] { "first", "second", "third" });

        Assert.Equal(output, DotNetRepositoryReleaseService.SummarizeProcessOutputLines(output));
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
