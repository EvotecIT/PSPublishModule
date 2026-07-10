using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseFreshBuildTests
{
    [Theory]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, false)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, true)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, true)]
    public void Execute_RebuildsStaleOutputBeforePacking(
        DotNetRepositoryPackStrategy packStrategy,
        bool targetFrameworkFromImport)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Stale"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Stale.csproj");
            var sourcePath = Path.Combine(projectDirectory.FullName, "Contract.cs");
            if (targetFrameworkFromImport)
            {
                File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), """
                    <Project>
                      <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                      </PropertyGroup>
                    </Project>
                    """);
            }

            var projectLines = new List<string>
            {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>"
            };
            if (!targetFrameworkFromImport)
                projectLines.Add("    <TargetFramework>net8.0</TargetFramework>");
            projectLines.AddRange(new[]
            {
                "    <PackageId>Sample.Stale</PackageId>",
                "    <VersionPrefix>1.0.0</VersionPrefix>",
                "    <IsPackable>true</IsPackable>",
                "  </PropertyGroup>",
                "</Project>"
            });
            File.WriteAllText(projectPath, string.Join(Environment.NewLine, projectLines));
            File.WriteAllText(sourcePath, "namespace Sample.Stale; public sealed class LegacyContract { }");

            RunDotNet(projectDirectory.FullName, "build", projectPath, "--configuration", "Release", "--nologo");
            var assemblyPath = Path.Combine(projectDirectory.FullName, "bin", "Release", "net8.0", "Sample.Stale.dll");
            Assert.True(File.Exists(assemblyPath));
            var staleAssembly = File.ReadAllBytes(assemblyPath);

            File.WriteAllText(sourcePath, "namespace Sample.Stale; public sealed class CurrentContract { }");
            File.SetLastWriteTimeUtc(sourcePath, File.GetLastWriteTimeUtc(assemblyPath).AddMinutes(-5));

            var outputPath = Path.Combine(root.FullName, "packages");
            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                Configuration = "Release",
                OutputPath = outputPath,
                Pack = true,
                PackStrategy = packStrategy,
                Publish = false,
                UpdateVersions = false,
                SignAssemblies = false,
                SignPackages = false
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotEqual(staleAssembly, File.ReadAllBytes(assemblyPath));
            var package = Assert.Single(Assert.Single(result.Projects, project => project.IsPackable).Packages);
            var typeNames = ReadPackagedTypeNames(package, "Sample.Stale.dll");
            Assert.Contains("Sample.Stale.CurrentContract", typeNames);
            Assert.DoesNotContain("Sample.Stale.LegacyContract", typeNames);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PackagePayloadValidation_RejectsAssemblyThatDoesNotMatchFreshBuild()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var packagePath = Path.Combine(root.FullName, "Sample.Stale.1.0.0.nupkg");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("lib/net8.0/Sample.Stale.dll");
                using var stream = entry.Open();
                stream.Write(new byte[] { 1, 2, 3 });
            }

            var outputHashes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sample.Stale.dll"] = new(StringComparer.OrdinalIgnoreCase) { ComputeSha256(new byte[] { 4, 5, 6 }) }
            };

            var success = DotNetRepositoryReleaseService.TryValidatePackagePayload(
                packagePath,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sample.Stale.dll" },
                outputHashes,
                out var validatedPayloads,
                out var error);

            Assert.False(success);
            Assert.Equal(0, validatedPayloads);
            Assert.Contains("does not match any freshly rebuilt output", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PackagePayloadValidation_IgnoresSameNamedNativeRuntimeAsset()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var managedPayload = new byte[] { 1, 2, 3 };
            var packagePath = Path.Combine(root.FullName, "Sample.Runtime.1.0.0.nupkg");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteArchiveEntry(archive, "lib/net8.0/Sample.Runtime.dll", managedPayload);
                WriteArchiveEntry(archive, "runtimes/win-x64/native/Sample.Runtime.dll", new byte[] { 4, 5, 6 });
            }

            var outputHashes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sample.Runtime.dll"] = new(StringComparer.OrdinalIgnoreCase) { ComputeSha256(managedPayload) }
            };

            var success = DotNetRepositoryReleaseService.TryValidatePackagePayload(
                packagePath,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sample.Runtime.dll" },
                outputHashes,
                out var validatedPayloads,
                out var error);

            Assert.True(success, error);
            Assert.Equal(1, validatedPayloads);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PackagePayloadValidation_ValidatesToolAssembly()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var toolPayload = new byte[] { 1, 2, 3 };
            var packagePath = Path.Combine(root.FullName, "Sample.Tool.1.0.0.nupkg");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
                WriteArchiveEntry(archive, "tools/net8.0/any/Sample.Tool.dll", toolPayload);

            var outputHashes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sample.Tool.dll"] = new(StringComparer.OrdinalIgnoreCase) { ComputeSha256(toolPayload) }
            };

            var success = DotNetRepositoryReleaseService.TryValidatePackagePayload(
                packagePath,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sample.Tool.dll" },
                outputHashes,
                out var validatedPayloads,
                out var error);

            Assert.True(success, error);
            Assert.Equal(1, validatedPayloads);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ProjectPackagePayloadValidation_RejectsStaleNestedPublishCopy()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Output"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Output.csproj");
            var sourcePath = Path.Combine(projectDirectory.FullName, "Contract.cs");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.Output</PackageId>
                    <VersionPrefix>1.0.0</VersionPrefix>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(sourcePath, "namespace Sample.Output; public sealed class LegacyContract { }");
            RunDotNet(projectDirectory.FullName, "build", projectPath, "--configuration", "Release", "--nologo");

            var targetDirectory = Path.Combine(projectDirectory.FullName, "bin", "Release", "net8.0");
            var assemblyPath = Path.Combine(targetDirectory, "Sample.Output.dll");
            var legacyAssembly = File.ReadAllBytes(assemblyPath);
            File.WriteAllText(sourcePath, "namespace Sample.Output; public sealed class CurrentContract { }");
            RunDotNet(projectDirectory.FullName, "build", projectPath, "--configuration", "Release", "--no-incremental", "--nologo");

            var publishDirectory = Directory.CreateDirectory(Path.Combine(targetDirectory, "publish"));
            File.WriteAllBytes(Path.Combine(publishDirectory.FullName, "Sample.Output.dll"), legacyAssembly);
            var packagePath = Path.Combine(root.FullName, "Sample.Output.1.0.0.nupkg");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
                WriteArchiveEntry(archive, "lib/net8.0/Sample.Output.dll", legacyAssembly);

            var success = DotNetRepositoryReleaseService.TryValidateProjectPackagePayloads(
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample.Output",
                    PackageId = "Sample.Output",
                    CsprojPath = projectPath,
                    IsPackable = true,
                    NewVersion = "1.0.0"
                },
                new DotNetRepositoryReleaseSpec
                {
                    RootPath = root.FullName,
                    Configuration = "Release"
                },
                new[] { packagePath },
                new NullLogger(),
                out var error);

            Assert.False(success);
            Assert.Contains("does not match any freshly rebuilt output", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ProjectPackagePayloadValidation_SkipsOutputLookupForMetadataOnlyPackage()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectPath = Path.Combine(root.FullName, "Sample.Metadata.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>");
            var packagePath = Path.Combine(root.FullName, "Sample.Metadata.1.0.0.nupkg");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
                WriteArchiveEntry(archive, "buildTransitive/Sample.Metadata.props", new byte[] { 1, 2, 3 });

            var success = DotNetRepositoryReleaseService.TryValidateProjectPackagePayloads(
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample.Metadata",
                    PackageId = "Sample.Metadata",
                    CsprojPath = projectPath,
                    IsPackable = true,
                    NewVersion = "1.0.0"
                },
                new DotNetRepositoryReleaseSpec
                {
                    RootPath = root.FullName,
                    Configuration = "Release"
                },
                new[] { packagePath },
                new NullLogger(),
                out var error);

            Assert.True(success, error);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static IReadOnlyList<string> ReadPackagedTypeNames(string packagePath, string assemblyFileName)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = Assert.Single(archive.Entries, candidate =>
            candidate.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(candidate.FullName), assemblyFileName, StringComparison.OrdinalIgnoreCase));
        using var stream = entry.Open();
        using var assembly = new MemoryStream();
        stream.CopyTo(assembly);
        assembly.Position = 0;
        using var peReader = new PEReader(assembly);
        var metadata = peReader.GetMetadataReader();
        return metadata.TypeDefinitions
            .Select(handle => metadata.GetTypeDefinition(handle))
            .Select(type => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}")
            .ToArray();
    }

    private static void RunDotNet(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutputTask, standardErrorTask);

        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', arguments)} failed.{Environment.NewLine}{standardOutputTask.Result}{Environment.NewLine}{standardErrorTask.Result}");
    }

    private static string ComputeSha256(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content));

    private static void WriteArchiveEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
