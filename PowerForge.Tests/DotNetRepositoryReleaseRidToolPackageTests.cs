using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseRidToolPackageTests
{
    private static readonly byte[] SignatureMarker = { 0x49, 0x58, 0x52, 0x49, 0x44 };

    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, true, false)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, true, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, true, true)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, true, true)]
    public void Execute_RidSpecificPackTool_PacksEveryRuntimeFromItsSignedPublishOutput(
        DotNetRepositoryPackStrategy packStrategy,
        bool signAssemblies,
        bool signDependencyAssemblies)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var dependencyDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Dependency"));
            File.WriteAllText(Path.Combine(dependencyDirectory.FullName, "Sample.Dependency.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <IsPackable>false</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(dependencyDirectory.FullName, "Dependency.cs"), "namespace Sample.Dependency; public static class Dependency { public static string Value => \"sample\"; }");

            // The SDK packs a pointer package plus one package per listed runtime. UseAppHost=false keeps the
            // runtime packages framework-dependent without downloading another platform's app host. The runtimes are
            // listed only for release evaluations, as a tool does to keep ordinary restores light, so every restore,
            // build, pack and evaluation in the release must carry the packaging property.
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Tool"));
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "Sample.Tool.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.Tool</PackageId>
                    <VersionPrefix>1.0.0</VersionPrefix>
                    <PackAsTool>true</PackAsTool>
                    <ToolCommandName>sample-tool</ToolCommandName>
                    <UseAppHost>false</UseAppHost>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(PowerForgePackaging)' == 'true'">
                    <ToolPackageRuntimeIdentifiers>linux-x64;any</ToolPackageRuntimeIdentifiers>
                    <RuntimeIdentifiers>$(ToolPackageRuntimeIdentifiers)</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\Sample.Dependency\Sample.Dependency.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "Program.cs"), "System.Console.WriteLine(Sample.Dependency.Dependency.Value);");

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
                    CertificateThumbprint = signAssemblies ? "ABC123" : null,
                    SignAssemblies = signAssemblies,
                    SignDependencyAssemblies = signDependencyAssemblies,
                    SignPackages = false
                },
                request =>
                {
                    signedPaths = Assert.IsType<string[]>(request.FilePaths);
                    foreach (var path in signedPaths)
                    {
                        // A signature unique to each file, so a package carrying another runtime's copy is caught.
                        var signature = SignatureMarker.Concat(Encoding.UTF8.GetBytes(Path.GetFullPath(path))).ToArray();
                        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
                        stream.Write(signature, 0, signature.Length);
                    }
                },
                _ => { });

            Assert.True(result.Success, result.ErrorMessage);
            var project = Assert.Single(result.Projects, project => project.ProjectName == "Sample.Tool");
            Assert.Equal(new[] { "linux-x64", "any" }, project.ToolPackageRuntimeIdentifiers);
            // Runtime packages come first, so a feed never lists the tool before the packages it points to.
            Assert.Equal(
                new[] { "Sample.Tool.linux-x64.1.0.0.nupkg", "Sample.Tool.any.1.0.0.nupkg", "Sample.Tool.1.0.0.nupkg" },
                project.Packages.Select(Path.GetFileName));

            foreach (var runtimeIdentifier in new[] { "linux-x64", "any" })
            {
                var package = project.Packages.Single(path => Path.GetFileName(path) == $"Sample.Tool.{runtimeIdentifier}.1.0.0.nupkg");
                // The dependency is signed in the runtime output even without SignDependencyAssemblies: the package
                // ships that copy, and ReadyToRun would have dropped any signature it had.
                foreach (var assembly in new[] { "Sample.Tool.dll", "Sample.Dependency.dll" })
                {
                    var stagedPath = Path.Combine(root.FullName, "Sample.Tool", "bin", "Release", "net8.0", runtimeIdentifier, "publish", assembly);
                    var payload = ReadEntry(package, $"tools/net8.0/{runtimeIdentifier}/{assembly}");
                    Assert.Equal(File.ReadAllBytes(stagedPath), payload);
                    var signature = SignatureMarker.Concat(Encoding.UTF8.GetBytes(Path.GetFullPath(stagedPath))).ToArray();
                    Assert.Equal(signAssemblies, payload.Skip(payload.Length - signature.Length).SequenceEqual(signature));
                }
            }

            using var pointer = ZipFile.OpenRead(project.Packages.Last());
            Assert.DoesNotContain(pointer.Entries, entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PublishPreflight_RejectsRidSpecificToolMissingARuntimePackage()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = new DotNetRepositoryProjectResult
            {
                ProjectName = "Sample.Tool",
                PackageId = "Sample.Tool",
                NewVersion = "1.2.3",
                ToolPackageRuntimeIdentifiers = { "linux-x64", "any" }
            };
            foreach (var packageName in new[] { "Sample.Tool.any.1.2.3.nupkg", "Sample.Tool.1.2.3.nupkg" })
            {
                var packagePath = Path.Combine(root.FullName, packageName);
                File.WriteAllBytes(packagePath, new byte[] { 1 });
                project.Packages.Add(packagePath);
            }

            var preflight = new DotNetRepositoryReleaseService(new NullLogger()).ValidatePublishPreflight(
                new[] { project },
                new DotNetRepositoryReleaseSpec { RootPath = root.FullName });

            Assert.False(preflight.Success);
            Assert.Contains("has no Sample.Tool.linux-x64 package to publish", preflight.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static byte[] ReadEntry(string packagePath, string entryName)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"{Path.GetFileName(packagePath)} has no {entryName}.");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
