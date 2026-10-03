using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class DotNetReleaseBuildContractTests
{
    [Fact]
    public void Execute_CreatesPortableReleaseZipAndNuGetPackage()
    {
        WithProject(project =>
        {
            var result = new DotNetReleaseBuildService(new NullLogger()).Execute(new DotNetReleaseBuildSpec { ProjectPath = project });
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Single(result.Packages);
            using var zip = ZipFile.OpenRead(result.ZipPath!);
            Assert.Contains(zip.Entries, entry => entry.FullName == "net8.0/Sample.dll");
            Assert.All(zip.Entries, entry => Assert.DoesNotContain('\\', entry.FullName));
        });
    }

    [Fact]
    public void Execute_MissingSigningHandlerPreservesExistingReleaseFiles()
    {
        WithProject(project =>
        {
            var release = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Release");
            Directory.CreateDirectory(release);
            var existing = Path.Combine(release, "keep.txt");
            File.WriteAllText(existing, "previous release");
            var result = new DotNetReleaseBuildService(new NullLogger()).Execute(new DotNetReleaseBuildSpec
            {
                ProjectPath = project,
                CertificateThumbprint = "MISSING"
            });
            Assert.False(result.Success);
            Assert.Contains("signing handler", result.ErrorMessage);
            Assert.Equal("previous release", File.ReadAllText(existing));
        });
    }

    private static void WithProject(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-single-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "Sample.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Sample.cs"), "public static class Sample { public static int Value => 42; }");
            action(project);
        }
        finally { Directory.Delete(root, true); }
    }
}
