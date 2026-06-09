using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectVersionServiceTests
{
    [Fact]
    public void Discover_returns_versions_from_supported_project_files()
    {
        var root = CreateTempDirectory();
        try
        {
            var moduleRoot = Path.Combine(root, "SampleModule");
            Directory.CreateDirectory(moduleRoot);
            File.WriteAllText(Path.Combine(moduleRoot, "SampleModule.csproj"), "<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(moduleRoot, "SampleModule.psd1"), "@{" + Environment.NewLine + "ModuleVersion = '1.2.3'" + Environment.NewLine + "}");
            File.WriteAllText(Path.Combine(moduleRoot, "Build-Module.ps1"), "Invoke-ModuleBuild -Settings {}" + Environment.NewLine + "ModuleVersion = '1.2.3'");

            var results = new ProjectVersionService().Discover(new ProjectVersionQueryRequest
            {
                RootPath = root
            });

            Assert.Equal(3, results.Count);
            Assert.Contains(results, entry => entry.Kind == ProjectVersionSourceKind.Csproj && entry.Version == "1.2.3");
            Assert.Contains(results, entry => entry.Kind == ProjectVersionSourceKind.PowerShellModule && entry.Version == "1.2.3");
            Assert.Contains(results, entry => entry.Kind == ProjectVersionSourceKind.BuildScript && entry.Version == "1.2.3");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void Update_increments_version_across_project_files()
    {
        var root = CreateTempDirectory();
        try
        {
            var moduleRoot = Path.Combine(root, "SampleModule");
            Directory.CreateDirectory(moduleRoot);
            var csprojPath = Path.Combine(moduleRoot, "SampleModule.csproj");
            var psd1Path = Path.Combine(moduleRoot, "SampleModule.psd1");
            var buildPath = Path.Combine(moduleRoot, "Build-Module.ps1");
            File.WriteAllText(csprojPath, "<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix><AssemblyVersion>1.2.3</AssemblyVersion></PropertyGroup></Project>");
            File.WriteAllText(psd1Path, "@{" + Environment.NewLine + "ModuleVersion = '1.2.3'" + Environment.NewLine + "}");
            File.WriteAllText(buildPath, "Invoke-ModuleBuild -Settings {}" + Environment.NewLine + "ModuleVersion = '1.2.3'");

            var results = new ProjectVersionService().Update(
                new ProjectVersionUpdateRequest
                {
                    RootPath = root,
                    ModuleName = "SampleModule",
                    IncrementKind = ProjectVersionIncrementKind.Minor
                },
                shouldProcess: (_, _) => true);

            Assert.Equal(3, results.Count);
            Assert.All(results, result => Assert.Equal(ProjectVersionUpdateStatus.Updated, result.Status));
            Assert.Contains("1.3.0", File.ReadAllText(csprojPath));
            Assert.Contains("1.3.0", File.ReadAllText(psd1Path));
            Assert.Contains("1.3.0", File.ReadAllText(buildPath));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void Update_returns_skipped_when_should_process_declines()
    {
        var root = CreateTempDirectory();
        try
        {
            var moduleRoot = Path.Combine(root, "SampleModule");
            Directory.CreateDirectory(moduleRoot);
            var csprojPath = Path.Combine(moduleRoot, "SampleModule.csproj");
            File.WriteAllText(csprojPath, "<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>");

            var results = new ProjectVersionService().Update(
                new ProjectVersionUpdateRequest
                {
                    RootPath = root,
                    ModuleName = "SampleModule",
                    NewVersion = "2.0.0"
                },
                shouldProcess: (_, _) => false);

            Assert.Single(results);
            Assert.Equal(ProjectVersionUpdateStatus.Skipped, results[0].Status);
            Assert.Contains("1.2.3", File.ReadAllText(csprojPath));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
