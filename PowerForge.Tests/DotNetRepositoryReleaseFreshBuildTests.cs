using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseFreshBuildTests
{
    [Fact]
    public void FreshnessCleanup_RemovesOnlyPrimaryAssembliesFromSelectedConfigurationOutputs()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Package"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Package.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <AssemblyName>Sample.Primary</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            var releaseFramework = Directory.CreateDirectory(Path.Combine(projectDirectory.FullName, "bin", "Release", "net8.0"));
            var releaseRid = Directory.CreateDirectory(Path.Combine(releaseFramework.FullName, "win-x64"));
            var debugFramework = Directory.CreateDirectory(Path.Combine(projectDirectory.FullName, "bin", "Debug", "net8.0"));
            var intermediate = Directory.CreateDirectory(Path.Combine(projectDirectory.FullName, "obj", "Release", "net8.0"));

            var staleFramework = Path.Combine(releaseFramework.FullName, "Sample.Primary.dll");
            var staleRid = Path.Combine(releaseRid.FullName, "Sample.Primary.dll");
            var staleProjectName = Path.Combine(releaseFramework.FullName, "Sample.Package.exe");
            var dependency = Path.Combine(releaseFramework.FullName, "Dependency.dll");
            var debugOutput = Path.Combine(debugFramework.FullName, "Sample.Primary.dll");
            var intermediateOutput = Path.Combine(intermediate.FullName, "Sample.Primary.dll");
            var intermediateCache = Path.Combine(intermediate.FullName, "project.assets.cache");
            foreach (var path in new[] { staleFramework, staleRid, staleProjectName, dependency, debugOutput, intermediateOutput, intermediateCache })
                File.WriteAllText(path, path);

            var success = DotNetRepositoryReleaseService.TryRemoveStalePrimaryPackageOutputs(
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample.Package",
                    CsprojPath = projectPath
                },
                "Release",
                new NullLogger(),
                out var removedFileCount,
                out var removedIntermediatePrimaryOutput,
                out _,
                out var error);

            Assert.True(success, error);
            Assert.Equal(4, removedFileCount);
            Assert.True(removedIntermediatePrimaryOutput);
            Assert.False(File.Exists(staleFramework));
            Assert.False(File.Exists(staleRid));
            Assert.False(File.Exists(staleProjectName));
            Assert.True(File.Exists(dependency));
            Assert.True(File.Exists(debugOutput));
            Assert.False(File.Exists(intermediateOutput));
            Assert.True(File.Exists(intermediateCache));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void FreshnessCleanup_DoesNotTreatAbandonedConventionalObjAsConfiguredIntermediateProof()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <BaseOutputPath>$(MSBuildThisFileDirectory)artifacts/bin/</BaseOutputPath>
                    <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)artifacts/obj/</BaseIntermediateOutputPath>
                  </PropertyGroup>
                </Project>
                """);
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Configured"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Configured.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <AssemblyName>Sample.Configured</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            var configuredOutput = Path.Combine(root.FullName, "artifacts", "bin", "Release", "net8.0", "Sample.Configured.dll");
            var abandonedIntermediate = Path.Combine(projectDirectory.FullName, "obj", "Release", "net8.0", "Sample.Configured.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(configuredOutput)!);
            Directory.CreateDirectory(Path.GetDirectoryName(abandonedIntermediate)!);
            File.WriteAllText(configuredOutput, "configured-output");
            File.WriteAllText(abandonedIntermediate, "abandoned-intermediate");

            var success = DotNetRepositoryReleaseService.TryRemoveStalePrimaryPackageOutputs(
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample.Configured",
                    CsprojPath = projectPath
                },
                "Release",
                new NullLogger(),
                out var removedFileCount,
                out var removedIntermediatePrimaryOutput,
                out _,
                out var error);

            Assert.True(success, error);
            Assert.Equal(1, removedFileCount);
            Assert.False(removedIntermediatePrimaryOutput);
            Assert.False(File.Exists(configuredOutput));
            Assert.True(File.Exists(abandonedIntermediate));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void FreshnessCleanup_DoesNotDeleteConditionalOutputFromAnotherConfiguration()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Conditional"));
            var debugOutputRoot = Path.Combine(root.FullName, "debug-only");
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Conditional.csproj");
            File.WriteAllText(projectPath, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                    <OutputPath>{debugOutputRoot}</OutputPath>
                  </PropertyGroup>
                </Project>
                """);

            var releaseOutput = Path.Combine(projectDirectory.FullName, "bin", "Release", "net8.0", "Sample.Conditional.dll");
            var releaseIntermediate = Path.Combine(projectDirectory.FullName, "obj", "Release", "net8.0", "Sample.Conditional.dll");
            var debugOutput = Path.Combine(debugOutputRoot, "Sample.Conditional.dll");
            foreach (var path in new[] { releaseOutput, releaseIntermediate, debugOutput })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, path);
            }

            var success = DotNetRepositoryReleaseService.TryRemoveStalePrimaryPackageOutputs(
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample.Conditional",
                    CsprojPath = projectPath
                },
                "Release",
                new NullLogger(),
                out var removedFileCount,
                out var removedIntermediatePrimaryOutput,
                out _,
                out var error);

            Assert.True(success, error);
            Assert.Equal(2, removedFileCount);
            Assert.True(removedIntermediatePrimaryOutput);
            Assert.False(File.Exists(releaseOutput));
            Assert.False(File.Exists(releaseIntermediate));
            Assert.True(File.Exists(debugOutput));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, false, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, false, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, true, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, true, false, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, false, true, false)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, false, true, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, false, false, true)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, false, false, true)]
    public void Execute_RecompilesStaleOutputBeforePacking(
        DotNetRepositoryPackStrategy packStrategy,
        bool targetFrameworkFromImport,
        bool centralOutputPaths,
        bool centralAssemblyName)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Stale"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Stale.csproj");
            var sourcePath = Path.Combine(projectDirectory.FullName, "Contract.cs");
            if (targetFrameworkFromImport || centralOutputPaths || centralAssemblyName)
            {
                var importedProperties = new List<string>();
                if (targetFrameworkFromImport)
                    importedProperties.Add("    <TargetFramework>net8.0</TargetFramework>");
                if (centralOutputPaths)
                {
                    importedProperties.Add("    <BaseOutputPath>$(MSBuildThisFileDirectory)artifacts/bin/</BaseOutputPath>");
                    importedProperties.Add("    <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)artifacts/obj/</BaseIntermediateOutputPath>");
                }
                if (centralAssemblyName)
                    importedProperties.Add("    <AssemblyName>Sample.Central</AssemblyName>");

                File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), string.Join(Environment.NewLine, new[]
                {
                    "<Project>",
                    "  <PropertyGroup>",
                    string.Join(Environment.NewLine, importedProperties),
                    "  </PropertyGroup>",
                    "</Project>"
                }));
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
            var assemblyFileName = centralAssemblyName ? "Sample.Central.dll" : "Sample.Stale.dll";
            var assemblyPath = centralOutputPaths
                ? Path.Combine(root.FullName, "artifacts", "bin", "Release", "net8.0", assemblyFileName)
                : Path.Combine(projectDirectory.FullName, "bin", "Release", "net8.0", assemblyFileName);
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
            var typeNames = ReadPackagedTypeNames(package, assemblyFileName);
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
