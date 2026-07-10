using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseFreshBuildTests
{
    [Theory]
    [InlineData(DotNetRepositoryPackStrategy.PerProject)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild)]
    public void Execute_RebuildsStaleOutputBeforePacking(DotNetRepositoryPackStrategy packStrategy)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Stale"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Stale.csproj");
            var sourcePath = Path.Combine(projectDirectory.FullName, "Contract.cs");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.Stale</PackageId>
                    <VersionPrefix>1.0.0</VersionPrefix>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
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
}
