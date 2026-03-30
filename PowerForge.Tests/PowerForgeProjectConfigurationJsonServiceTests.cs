namespace PowerForge.Tests;

public sealed class PowerForgeProjectConfigurationJsonServiceTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsProjectConfiguration()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(tempRoot, "project.release.json");
            var project = new ConfigurationProject
            {
                Name = "PSPublishModule",
                ProjectRoot = ".",
                Release = new ConfigurationProjectRelease
                {
                    Configuration = "Release",
                    PublishToolGitHub = true,
                    SkipRestore = true,
                    ToolOutput = new[] { ConfigurationProjectReleaseOutputType.Portable }
                },
                Signing = new ConfigurationProjectSigning
                {
                    Mode = ConfigurationProjectSigningMode.OnDemand,
                    Description = "PowerForge"
                },
                Targets = new[]
                {
                    new ConfigurationProjectTarget
                    {
                        Name = "Cli",
                        ProjectPath = "src/Cli/Cli.csproj",
                        Framework = "net10.0",
                        Runtimes = new[] { "win-x64" },
                        OutputType = new[]
                        {
                            ConfigurationProjectTargetOutputType.Tool,
                            ConfigurationProjectTargetOutputType.Portable
                        }
                    }
                }
            };

            var service = new PowerForgeProjectConfigurationJsonService();
            var savedPath = service.Save(project, path, overwrite: false);
            var loaded = service.Load(savedPath);

            Assert.Equal(Path.GetFullPath(path), savedPath);
            Assert.Equal("PSPublishModule", loaded.Name);
            Assert.Equal(Path.GetFullPath(tempRoot), loaded.ProjectRoot);
            Assert.Equal("Release", loaded.Release.Configuration);
            Assert.True(loaded.Release.PublishToolGitHub);
            Assert.True(loaded.Release.SkipRestore);
            Assert.Single(loaded.Release.ToolOutput);
            Assert.Equal(ConfigurationProjectReleaseOutputType.Portable, loaded.Release.ToolOutput[0]);
            Assert.Single(loaded.Targets);
            Assert.Equal("Cli", loaded.Targets[0].Name);
            Assert.Equal("win-x64", loaded.Targets[0].Runtimes[0]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Load_AllowsCommentsAndTrailingCommas()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var path = Path.Combine(tempRoot, "project.release.json");
            File.WriteAllText(path, """
                {
                  // project comment
                  "Name": "Demo",
                  "Release": {
                    "Configuration": "Release",
                    "ToolOutput": [ "Portable", ],
                  },
                  "Targets": [
                    {
                      "Name": "Cli",
                      "ProjectPath": "src/Cli/Cli.csproj",
                      "Framework": "net10.0",
                      "Runtimes": [ "win-x64", ],
                    },
                  ],
                }
                """);

            var service = new PowerForgeProjectConfigurationJsonService();
            var loaded = service.Load(path);

            Assert.Equal("Demo", loaded.Name);
            Assert.Single(loaded.Release.ToolOutput);
            Assert.Equal(ConfigurationProjectReleaseOutputType.Portable, loaded.Release.ToolOutput[0]);
            Assert.Single(loaded.Targets);
            Assert.Equal("win-x64", loaded.Targets[0].Runtimes[0]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Load_ResolvesRelativeProjectRootAgainstConfigDirectory()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var buildDirectory = Path.Combine(tempRoot, "Build");
            Directory.CreateDirectory(buildDirectory);
            var path = Path.Combine(buildDirectory, "project.release.json");
            File.WriteAllText(path, """
                {
                  "Name": "Demo",
                  "ProjectRoot": "..",
                  "Targets": [
                    {
                      "Name": "Cli",
                      "ProjectPath": "src/Cli/Cli.csproj",
                      "Framework": "net10.0",
                      "Runtimes": [ "win-x64" ]
                    }
                  ]
                }
                """);

            var service = new PowerForgeProjectConfigurationJsonService();
            var loaded = service.Load(path);

            Assert.Equal(Path.GetFullPath(tempRoot), loaded.ProjectRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeProjectJson-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
