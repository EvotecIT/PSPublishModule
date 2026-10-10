using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseToolSigningTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, true, false)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, true, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, true, true)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, true, true)]
    public void Execute_WithAssemblySigning_PreservesSignedPackToolAssembly(
        DotNetRepositoryPackStrategy packStrategy,
        bool signDependencyAssemblies,
        bool usePackConditionedPublishDirectory)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var dependencyDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Dependency"));
            var dependencyProjectPath = Path.Combine(dependencyDirectory.FullName, "Sample.Dependency.csproj");
            File.WriteAllText(dependencyProjectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <IsPackable>false</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(dependencyDirectory.FullName, "Dependency.cs"), "namespace Sample.Dependency; public static class Dependency { public static string Value => \"sample\"; }");

            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Tool"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Tool.csproj");
            var projectLines = new List<string>
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <OutputType>Exe</OutputType>",
                "    <TargetFramework>net8.0</TargetFramework>",
                "    <PackageId>Sample.Tool</PackageId>",
                "    <VersionPrefix>1.0.0</VersionPrefix>",
                "    <PackAsTool>true</PackAsTool>",
                "    <ToolCommandName>sample-tool</ToolCommandName>"
            };
            if (usePackConditionedPublishDirectory)
                projectLines.Add("    <PublishDir Condition=\"'$(_IsPacking)' == 'true' And '$(NoBuild)' == 'true' And '$(BuildProjectReferences)' == 'false' And '$(PackageOutputPath)' != ''\">$(PackageOutputPath)\\conditioned-publish\\</PublishDir>");
            if (signDependencyAssemblies)
            {
                var obsoletePublishDirectory = usePackConditionedPublishDirectory
                    // PackageOutputPath is unset during the initial build.
                    ? "$(MSBuildProjectDirectory)/../packages/conditioned-publish/"
                    : "$(OutputPath)publish\\";
                projectLines.Add($"    <ObsoletePublishDir>{obsoletePublishDirectory}</ObsoletePublishDir>");
            }
            projectLines.AddRange(new[]
            {
                "  </PropertyGroup>",
                "  <ItemGroup>",
                "    <ProjectReference Include=\"..\\Sample.Dependency\\Sample.Dependency.csproj\" />",
                "  </ItemGroup>"
            });
            if (signDependencyAssemblies)
            {
                projectLines.AddRange(new[]
                {
                    "  <ItemGroup>",
                    "    <None Update=\"Sample.Runtime.dll\" CopyToPublishDirectory=\"PreserveNewest\" TargetPath=\"runtimes/win-x64/lib/net8.0/Sample.Runtime.dll\" />",
                    "  </ItemGroup>",
                    "  <Target Name=\"SeedObsoletePublishDependency\" AfterTargets=\"Build\">",
                    "    <MakeDir Directories=\"$(ObsoletePublishDir)\" />",
                    "    <WriteLinesToFile File=\"$(ObsoletePublishDir)Obsolete.Dependency.dll\" Lines=\"obsolete\" Overwrite=\"true\" />",
                    "    <MakeDir Directories=\"$(OutputPath)osx-arm64\" />",
                    "    <WriteLinesToFile File=\"$(OutputPath)osx-arm64/Obsolete.Rid.Dependency.dll\" Lines=\"unshipped platform output\" Overwrite=\"true\" />",
                    "  </Target>"
                });
            }
            projectLines.Add("</Project>");
            File.WriteAllText(projectPath, string.Join(Environment.NewLine, projectLines));
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "Program.cs"), "System.Console.WriteLine(Sample.Dependency.Dependency.Value);");
            if (signDependencyAssemblies)
                File.WriteAllBytes(Path.Combine(projectDirectory.FullName, "Sample.Runtime.dll"), new byte[] { 1, 2, 3, 4 });

            var marker = new byte[] { 0x49, 0x58, 0x53, 0x49, 0x47 };
            string[] signedPaths = Array.Empty<string>();
            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                new DotNetRepositoryReleaseSpec
                {
                    RootPath = root.FullName,
                    Configuration = "Release",
                    OutputPath = Path.Combine(root.FullName, "packages"),
                    Pack = true,
                    PackStrategy = packStrategy,
                    Publish = false,
                    UpdateVersions = false,
                    CreateReleaseZip = false,
                    CertificateThumbprint = "ABC123",
                    SignAssemblies = true,
                    SignDependencyAssemblies = signDependencyAssemblies,
                    SignPackages = false
                },
                request =>
                {
                    signedPaths = Assert.IsType<string[]>(request.FilePaths);
                    foreach (var path in signedPaths.Where(path =>
                                 path.EndsWith("Sample.Tool.dll", StringComparison.OrdinalIgnoreCase) ||
                                 path.EndsWith("Sample.Dependency.dll", StringComparison.OrdinalIgnoreCase) ||
                                 path.EndsWith("Sample.Runtime.dll", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
                        stream.Write(marker, 0, marker.Length);
                    }
                },
                _ => { });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(signedPaths, path => path.EndsWith(Path.Combine("obj", "Release", "net8.0", "Sample.Tool.dll"), StringComparison.OrdinalIgnoreCase));
            if (signDependencyAssemblies)
            {
                var expectedPublishDirectory = usePackConditionedPublishDirectory ? "conditioned-publish" : "publish";
                Assert.Contains(signedPaths, path => path.EndsWith(Path.Combine(expectedPublishDirectory, "Sample.Dependency.dll"), StringComparison.OrdinalIgnoreCase));
                Assert.Contains(signedPaths, path => path.EndsWith(Path.Combine("runtimes", "win-x64", "lib", "net8.0", "Sample.Runtime.dll"), StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(signedPaths, path => path.EndsWith("Obsolete.Rid.Dependency.dll", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(signedPaths, path => path.EndsWith("Obsolete.Dependency.dll", StringComparison.OrdinalIgnoreCase));
            }
            else
                Assert.DoesNotContain(signedPaths, path => path.EndsWith("Sample.Dependency.dll", StringComparison.OrdinalIgnoreCase));

            var package = Assert.Single(Assert.Single(result.Projects, project => project.IsPackable).Packages);
            using var archive = ZipFile.OpenRead(package);
            Assert.DoesNotContain(archive.Entries, item => item.FullName.EndsWith("Obsolete.Dependency.dll", StringComparison.OrdinalIgnoreCase));
            var signedEntryNames = signDependencyAssemblies
                ? new[] { "Sample.Tool.dll", "Sample.Dependency.dll", "runtimes/win-x64/lib/net8.0/Sample.Runtime.dll" }
                : new[] { "Sample.Tool.dll" };
            foreach (var entryName in signedEntryNames)
            {
                var entry = Assert.Single(archive.Entries, item => string.Equals(item.FullName, $"tools/net8.0/any/{entryName}", StringComparison.OrdinalIgnoreCase));
                using var entryStream = entry.Open();
                using var packagedAssembly = new MemoryStream();
                entryStream.CopyTo(packagedAssembly);
                Assert.Equal(marker, packagedAssembly.ToArray().TakeLast(marker.Length).ToArray());
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

}
