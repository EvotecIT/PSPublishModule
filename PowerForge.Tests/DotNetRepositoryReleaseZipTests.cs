using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseZipTests {
    [Fact]
    public void Execute_CreateReleaseZip_ExcludesStaleFrameworkAndNestedPublishOutputs() {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Package"));
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "Sample.Package.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.Package</PackageId>
                    <VersionPrefix>1.0.0</VersionPrefix>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Include="content/current.txt" CopyToOutputDirectory="PreserveNewest" />
                    <None Include="content/publish/required.txt" CopyToOutputDirectory="PreserveNewest" TargetPath="publish/required.txt" />
                    <None Include="content/runtimes/win-x64/native/native-dependency.bin" CopyToOutputDirectory="PreserveNewest" TargetPath="runtimes/win-x64/native/native-dependency.bin" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(projectDirectory.FullName, "Class1.cs"),
                "namespace Sample.Package; public static class Class1 { public static string Value => \"current\"; }");
            var contentDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory.FullName, "content"));
            File.WriteAllText(Path.Combine(contentDirectory.FullName, "current.txt"), "current-build-content");
            var publishContentDirectory = Directory.CreateDirectory(Path.Combine(contentDirectory.FullName, "publish"));
            File.WriteAllText(Path.Combine(publishContentDirectory.FullName, "required.txt"), "required-publish-content");
            var runtimeContentDirectory = Directory.CreateDirectory(Path.Combine(contentDirectory.FullName, "runtimes", "win-x64", "native"));
            File.WriteAllText(Path.Combine(runtimeContentDirectory.FullName, "native-dependency.bin"), "required-runtime-content");

            var currentOutput = Directory.CreateDirectory(Path.Combine(projectDirectory.FullName, "bin", "Release", "net8.0"));
            var staleRuntimeOutput = Directory.CreateDirectory(Path.Combine(currentOutput.FullName, "win-x64"));
            File.WriteAllText(Path.Combine(staleRuntimeOutput.FullName, "stale-runtime.bin"), "must-not-ship");
            var stalePublishOutput = Directory.CreateDirectory(Path.Combine(currentOutput.FullName, "publish"));
            File.WriteAllText(Path.Combine(stalePublishOutput.FullName, "stale-publish.bin"), "must-not-ship");
            var staleNestedFrameworkOutput = Directory.CreateDirectory(Path.Combine(currentOutput.FullName, "legacy-output"));
            File.WriteAllText(Path.Combine(staleNestedFrameworkOutput.FullName, "Sample.Package.deps.json"), "stale-output-marker");
            File.WriteAllText(Path.Combine(staleNestedFrameworkOutput.FullName, "stale-nested.bin"), "must-not-ship");
            var staleFrameworkOutput = Directory.CreateDirectory(Path.Combine(projectDirectory.FullName, "bin", "Release", "net9.0"));
            File.WriteAllText(Path.Combine(staleFrameworkOutput.FullName, "stale-framework.bin"), "must-not-ship");

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = Path.Combine(root.FullName, "packages"),
                ReleaseZipOutputPath = Path.Combine(root.FullName, "releases"),
                Pack = true,
                Publish = false,
                UpdateVersions = false,
                CreateReleaseZip = true
            });

            Assert.True(result.Success, result.ErrorMessage);
            var project = Assert.Single(result.Projects, item => item.IsPackable);
            Assert.True(File.Exists(project.ReleaseZipPath));
            using var archive = ZipFile.OpenRead(project.ReleaseZipPath!);
            var entries = archive.Entries
                .Where(static entry => !string.IsNullOrEmpty(entry.Name))
                .Select(static entry => entry.FullName.Replace('\\', '/'))
                .ToArray();
            Assert.Contains("net8.0/Sample.Package.dll", entries);
            Assert.Contains("net8.0/content/current.txt", entries);
            Assert.Contains("net8.0/publish/required.txt", entries);
            Assert.Contains("net8.0/runtimes/win-x64/native/native-dependency.bin", entries);
            Assert.DoesNotContain("net8.0/win-x64/stale-runtime.bin", entries);
            Assert.DoesNotContain(entries, static entry => entry.Contains("legacy-output", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, static entry => entry.Contains("net9.0", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, static entry => entry.Contains("stale", StringComparison.OrdinalIgnoreCase));
        } finally {
            try {
                root.Delete(recursive: true);
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void ReleaseZipSourceTopology_DetectsOverlappingFrameworkOutputRoots() {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try {
            var frameworkOutput = Directory.CreateDirectory(Path.Combine(root.FullName, "shared"));
            var nestedFrameworkOutput = Directory.CreateDirectory(Path.Combine(frameworkOutput.FullName, "nested"));

            Assert.True(DotNetRepositoryReleaseService.ReleaseZipPathsOverlap(
                frameworkOutput.FullName,
                nestedFrameworkOutput.FullName));
            Assert.True(DotNetRepositoryReleaseService.ReleaseZipPathsOverlap(
                nestedFrameworkOutput.FullName,
                frameworkOutput.FullName));
        } finally {
            try {
                root.Delete(recursive: true);
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void ReleaseZipPathValidation_RejectsLinkedTargetDirectory() {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var outside = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try {
            var linkedOutput = Path.Combine(root.FullName, "linked-output");
            try {
                Directory.CreateSymbolicLink(linkedOutput, outside.FullName);
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException) {
                return;
            }

            Assert.False(DotNetRepositoryReleaseService.TryValidateReleaseZipPath(linkedOutput, out var error));
            Assert.Contains("linked", error, StringComparison.OrdinalIgnoreCase);
        } finally {
            try {
                root.Delete(recursive: true);
            } catch {
                // Best-effort test cleanup.
            }
            try {
                outside.Delete(recursive: true);
            } catch {
                // Best-effort test cleanup.
            }
        }
    }
}
