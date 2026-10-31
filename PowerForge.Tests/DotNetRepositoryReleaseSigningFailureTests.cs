using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseSigningFailureTests
{
    [Theory]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, false)]
    [InlineData(DotNetRepositoryPackStrategy.PerProject, true)]
    [InlineData(DotNetRepositoryPackStrategy.MSBuild, false)]
    public void Execute_AssemblySigningFailureStopsLaterProjectsAndPackageSigning(DotNetRepositoryPackStrategy strategy, bool failFast)
    {
        var root = Directory.CreateTempSubdirectory("PowerForge.SigningFailure.");
        try
        {
            foreach (var name in new[] { "First", "Second" })
            {
                var project = root.CreateSubdirectory(name);
                File.WriteAllText(Path.Combine(project.FullName, name + ".csproj"), """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                        <VersionPrefix>1.0.0</VersionPrefix>
                        <IsPackable>true</IsPackable>
                      </PropertyGroup>
                    </Project>
                    """);
                File.WriteAllText(Path.Combine(project.FullName, "Sample.cs"), "public class Sample { }");
            }
            var assemblyCalls = 0;
            var packageCalls = 0;
            DotNetRepositoryReleaseService.PackageSigningHandler packageSigner = (
                IReadOnlyList<string> packages, DotNetRepositoryReleaseSpec spec, string sha256,
                Action<string, int, DotNetNuGetSignResult?>? progress, out string[] failed, out string error) =>
            {
                packageCalls++;
                failed = packages.ToArray();
                error = "Unexpected package signing";
                return false;
            };
            var service = new DotNetRepositoryReleaseService(new NullLogger(), packageSigner, (_, _) => "ABCDEF");
            var result = service.Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                OutputPath = Path.Combine(root.FullName, "packages"),
                PackStrategy = strategy,
                Pack = true,
                UpdateVersions = false,
                Publish = false,
                CreateReleaseZip = false,
                CertificateThumbprint = "ABC123",
                SignAssemblies = true,
                SignPackages = true,
                PublishFailFast = failFast
            }, request =>
            {
                assemblyCalls++;
                var files = Assert.IsType<string[]>(request.FilePaths);
                Assert.NotEmpty(files);
                Assert.All(files, path => Assert.True(File.Exists(path), path));
                throw new InvalidOperationException("Synthetic signing denial");
            }, _ => { });

            Assert.False(result.Success);
            Assert.Equal(2, result.Projects.Count(project => project.IsPackable));
            Assert.Equal(1, assemblyCalls);
            Assert.Equal(0, packageCalls);
            Assert.All(result.Projects, project => Assert.Empty(project.Packages));
            Assert.Empty(result.PublishedPackages);
            Assert.Contains("Synthetic signing denial", result.ErrorMessage, StringComparison.Ordinal);
        }
        finally { root.Delete(recursive: true); }
    }
}
