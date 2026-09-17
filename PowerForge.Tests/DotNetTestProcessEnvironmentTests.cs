using System.Diagnostics;

namespace PowerForge.Tests;

public sealed class DotNetTestProcessEnvironmentTests
{
    [Fact]
    public void DisableBuildServers_uses_supported_dotnet_and_msbuild_switches()
    {
        var startInfo = new ProcessStartInfo();
        startInfo.Environment.Remove("DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER");

        DotNetTestProcessEnvironment.DisableBuildServers(startInfo);

        Assert.Equal("0", startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"]);
        Assert.Equal("1", startInfo.Environment["MSBUILDDISABLENODEREUSE"]);
        Assert.False(startInfo.Environment.ContainsKey("DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"));
    }
}
