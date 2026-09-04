using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void ValidateReleaseOutputDestinations_RejectsGeneratedChecksumOverStagedMetadata()
    {
        string root = CreateSandbox();
        try
        {
            string source = Path.Combine(root, "dotnet", "SHA256SUMS.txt");
            string destination = Path.Combine(root, "release", "SHA256SUMS.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "dotnet-checksums", new UTF8Encoding(false));
            var entry = new PowerForgeReleaseAssetEntry
            {
                Path = source,
                StagedPath = destination,
                Category = PowerForgeReleaseAssetCategory.Metadata,
                Source = "DotNetPublish"
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.ValidateReleaseOutputDestinations(
                    [entry],
                    manifestPath: Path.Combine(root, "release", "release-manifest.json"),
                    checksumsPath: destination));

            Assert.Contains("collides with staged asset", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(source, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_StageRoot_RejectsDistinctAssetsWithSameDestinationBeforeCopying()
    {
        string root = CreateSandbox();
        string firstDirectory = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        string secondDirectory = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        string firstPackage = Path.Combine(firstDirectory, "Sample.1.0.0.nupkg");
        string secondPackage = Path.Combine(secondDirectory, "Sample.1.0.0.nupkg");
        File.WriteAllText(firstPackage, "first", new UTF8Encoding(false));
        File.WriteAllText(secondPackage, "second", new UTF8Encoding(false));

        try
        {
            PowerForgeReleaseService service = CreatePackageReleaseService(firstPackage, secondPackage);
            string stageRoot = Path.Combine(root, "release");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = ".",
                        Configuration = "Release"
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    StageRoot = stageRoot
                }));

            Assert.Contains("staging destination collision", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(firstPackage, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(secondPackage, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(stageRoot, "nuget", "Sample.1.0.0.nupkg")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_StageRoot_RejectsGeneratedOutputOverOriginalAssetBeforeCopying()
    {
        string root = CreateSandbox();
        string package = Path.Combine(root, "Sample.1.0.0.nupkg");
        File.WriteAllText(package, "original-package", new UTF8Encoding(false));
        try
        {
            string stageRoot = Path.Combine(root, "release");
            PowerForgeReleaseService service = CreatePackageReleaseService(package);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                CreatePackageReleaseSpec(),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    StageRoot = stageRoot,
                    ChecksumsPath = package
                }));

            Assert.Contains("collides with staged asset", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("original-package", File.ReadAllText(package));
            Assert.False(File.Exists(Path.Combine(stageRoot, "nuget", Path.GetFileName(package))));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_StageRoot_RejectsAssetOverGeneratedOutputBeforeCopying()
    {
        string root = CreateSandbox();
        string package = Path.Combine(root, "source", "Sample.1.0.0.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        File.WriteAllText(package, "original-package", new UTF8Encoding(false));
        try
        {
            string stageRoot = Path.Combine(root, "release");
            string checksumsPath = Path.Combine(stageRoot, "nuget", Path.GetFileName(package));
            Directory.CreateDirectory(Path.GetDirectoryName(checksumsPath)!);
            File.WriteAllText(checksumsPath, "existing-checksums", new UTF8Encoding(false));
            PowerForgeReleaseService service = CreatePackageReleaseService(package);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                CreatePackageReleaseSpec(),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    StageRoot = stageRoot,
                    ChecksumsPath = checksumsPath
                }));

            Assert.Contains("collides with staged asset", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("existing-checksums", File.ReadAllText(checksumsPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static PowerForgeReleaseSpec CreatePackageReleaseSpec()
        => new()
        {
            Packages = new ProjectBuildConfiguration
            {
                RootPath = ".",
                Configuration = "Release"
            }
        };

    private static PowerForgeReleaseService CreatePackageReleaseService(params string[] packagePaths)
        => new(
            new NullLogger(),
            executePackages: (_, _, _) =>
            {
                var release = new DotNetRepositoryReleaseResult();
                var project = new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample",
                    PackageId = "Sample",
                    IsPackable = true,
                    NewVersion = "1.0.0"
                };
                foreach (string packagePath in packagePaths)
                {
                    project.Packages.Add(packagePath);
                }
                release.Projects.Add(project);
                return new ProjectBuildHostExecutionResult
                {
                    Success = true,
                    ConfigPath = "release.json",
                    Result = new ProjectBuildResult
                    {
                        Success = true,
                        Release = release
                    }
                };
            },
            planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
            runTools: _ => throw new InvalidOperationException("Tools should not run."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));
}
