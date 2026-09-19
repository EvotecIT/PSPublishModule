using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerWatchTests
{
    [Fact]
    public void WatchIgnoresGeneratedFileAsWellAsDirectoryContents()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pf-web-watch");
        string file = Path.Combine(directory, "release-hub.json");
        string root = file + Path.DirectorySeparatorChar;

        Assert.True(WebPipelineRunner.IsUnderAnyRoot(file, [root]));
        Assert.True(WebPipelineRunner.IsUnderAnyRoot(Path.Combine(file, "child"), [root]));
        Assert.False(WebPipelineRunner.IsUnderAnyRoot(file + ".backup", [root]));
    }
}
