namespace PowerForge.Tests;

public sealed class PowerForgeProjectConfigurationScaffoldServiceTests
{
    [Fact]
    public void Generate_WritesStarterConfigWithRelativeProjectRoot()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var sourceDirectory = Path.Combine(tempRoot, "src", "Cli");
            Directory.CreateDirectory(sourceDirectory);
            var projectPath = Path.Combine(sourceDirectory, "Cli.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
                  </PropertyGroup>
                </Project>
                """);

            var service = new PowerForgeProjectConfigurationScaffoldService();
            var result = service.Generate(new PowerForgeProjectConfigurationScaffoldRequest
            {
                ProjectRoot = tempRoot,
                OutputPath = Path.Combine("Build", "project.release.json"),
                IncludePortableOutput = true,
                WorkingDirectory = tempRoot
            });

            var jsonService = new PowerForgeProjectConfigurationJsonService();
            var project = jsonService.Load(result.ConfigPath);

            Assert.Equal(Path.Combine(tempRoot, "Build", "project.release.json"), result.ConfigPath);
            Assert.Equal("Cli", result.TargetName);
            Assert.Equal("net10.0", result.Framework);
            Assert.Equal(2, result.Runtimes.Length);
            Assert.True(result.IncludesPortableOutput);

            Assert.Equal(Path.GetFullPath(tempRoot), project.ProjectRoot);
            Assert.Equal("Cli", project.Name);
            Assert.Single(project.Targets);
            Assert.Equal("src/Cli/Cli.csproj", project.Targets[0].ProjectPath.Replace('\\', '/'));
            Assert.Contains(ConfigurationProjectReleaseOutputType.Portable, project.Release.ToolOutput);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeProjectScaffold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
