using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class ProcessRunnerNet472SmokeTests
{
    [Fact]
    public void WindowsTaskKillPathUsesSystemDirectory()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return;

        var path = ProcessRunner.GetWindowsTaskKillPath();

        Assert.Equal(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), path, StringComparer.OrdinalIgnoreCase);
        Assert.True(Path.IsPathRooted(path));
        Assert.True(File.Exists(path));
    }
}
