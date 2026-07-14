using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseTargetFrameworkTests
{
    [Fact]
    public void ProjectPackagePayloadValidation_IncludesFrameworksAddedByConditionalTargetFrameworks()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.Conditional"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.Conditional.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0</TargetFrameworks>
                    <TargetFrameworks Condition="'$(Configuration)' == 'Release'">$(TargetFrameworks);netstandard2.0</TargetFrameworks>
                    <AssemblyName>Sample.Conditional</AssemblyName>
                    <PackageId>Sample.Conditional</PackageId>
                    <VersionPrefix>1.0.0</VersionPrefix>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);

            var net8Output = Directory.CreateDirectory(Path.Combine(projectDirectory.FullName, "bin", "Release", "net8.0"));
            File.WriteAllBytes(Path.Combine(net8Output.FullName, "Sample.Conditional.dll"), new byte[] { 1, 2, 3 });
            var conditionalOutput = Directory.CreateDirectory(Path.Combine(projectDirectory.FullName, "bin", "Release", "netstandard2.0"));
            var conditionalAssembly = new byte[] { 4, 5, 6 };
            File.WriteAllBytes(Path.Combine(conditionalOutput.FullName, "Sample.Conditional.dll"), conditionalAssembly);

            var packagePath = Path.Combine(root.FullName, "Sample.Conditional.1.0.0.nupkg");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
                WriteArchiveEntry(archive, "lib/netstandard2.0/Sample.Conditional.dll", conditionalAssembly);

            var success = DotNetRepositoryReleaseService.TryValidateProjectPackagePayloads(
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample.Conditional",
                    PackageId = "Sample.Conditional",
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

    private static void WriteArchiveEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
