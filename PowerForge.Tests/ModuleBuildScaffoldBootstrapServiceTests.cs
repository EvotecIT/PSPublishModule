using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleBuildScaffoldBootstrapServiceTests
{
    [Fact]
    public void EnsureScaffold_returns_failure_when_base_path_is_missing()
    {
        var logger = new BufferedLogger();
        var service = new ModuleBuildScaffoldBootstrapService(logger);
        var missingBasePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = service.EnsureScaffold(new ModuleBuildPreparedContext
        {
            ModuleName = "SampleModule",
            ProjectRoot = Path.Combine(missingBasePath, "SampleModule"),
            BasePathForScaffold = missingBasePath
        }, moduleBase: null);

        Assert.False(result.Succeeded);
        Assert.False(result.Attempted);
        Assert.Contains(logger.Entries, entry => entry.Level == "error" && entry.Message.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureScaffold_creates_project_when_base_path_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var moduleBase = Path.Combine(root, "ModuleBase");
            var data = Path.Combine(moduleBase, "Data");
            Directory.CreateDirectory(data);

            File.WriteAllText(Path.Combine(data, "Example-Gitignore.txt"), string.Empty);
            File.WriteAllText(Path.Combine(data, "Example-CHANGELOG.MD"), string.Empty);
            File.WriteAllText(Path.Combine(data, "Example-README.MD"), string.Empty);
            File.WriteAllText(Path.Combine(data, "Example-LicenseMIT.txt"), string.Empty);
            File.WriteAllText(Path.Combine(data, "Example-ModuleBuilder.txt"), "`$GUID`n`$ModuleName");
            File.WriteAllText(Path.Combine(data, "Example-ModulePSM1.txt"), string.Empty);
            File.WriteAllText(Path.Combine(data, "Example-ModulePSD1.txt"), "`$GUID`n`$ModuleName");

            var logger = new NullLogger();
            var service = new ModuleBuildScaffoldBootstrapService(logger);
            var projectRoot = Path.Combine(root, "SampleModule");

            var result = service.EnsureScaffold(new ModuleBuildPreparedContext
            {
                ModuleName = "SampleModule",
                ProjectRoot = projectRoot,
                BasePathForScaffold = root
            }, moduleBase);

            Assert.True(result.Succeeded);
            Assert.True(result.Attempted);
            Assert.True(Directory.Exists(projectRoot));
            Assert.True(File.Exists(Path.Combine(projectRoot, "SampleModule.psd1")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
