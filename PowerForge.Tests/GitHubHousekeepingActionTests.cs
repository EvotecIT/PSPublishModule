namespace PowerForge.Tests;

public sealed class GitHubHousekeepingActionTests
{
    [Fact]
    public void CompositeAction_AssetPaths_ShouldResolveFromActionDirectory()
    {
        var repoRoot = FindRepoRoot();
        var actionRoot = Path.Combine(repoRoot, ".github", "actions", "github-housekeeping");
        var actionYamlPath = Path.Combine(actionRoot, "action.yml");
        var scriptPath = Path.Combine(actionRoot, "Invoke-PowerForgeHousekeeping.ps1");

        Assert.True(Directory.Exists(actionRoot), $"Action directory not found: {actionRoot}");
        Assert.True(File.Exists(actionYamlPath), $"Composite action definition not found: {actionYamlPath}");
        Assert.True(File.Exists(scriptPath), $"Composite action script not found: {scriptPath}");

        var globalJsonPath = Path.GetFullPath(Path.Combine(actionRoot, "..", "..", "..", "global.json"));
        var cliProjectPath = Path.GetFullPath(Path.Combine(actionRoot, "..", "..", "..", "PowerForge.Cli", "PowerForge.Cli.csproj"));

        Assert.True(File.Exists(globalJsonPath), $"global.json should resolve from composite action directory: {globalJsonPath}");
        Assert.True(File.Exists(cliProjectPath), $"PowerForge.Cli project should resolve from composite action directory: {cliProjectPath}");

        var actionYaml = File.ReadAllText(actionYamlPath);
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("../../../global.json", actionYaml, StringComparison.Ordinal);
        Assert.Contains("../../..", actionYaml, StringComparison.Ordinal);
        Assert.Contains("../../..", script, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            var marker = Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj");
            if (File.Exists(marker))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for GitHub housekeeping action tests.");
    }
}
