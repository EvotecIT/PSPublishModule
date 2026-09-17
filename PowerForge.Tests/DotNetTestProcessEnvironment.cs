using System.Diagnostics;

namespace PowerForge.Tests;

internal static class DotNetTestProcessEnvironment
{
    internal static void DisableBuildServers(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
    }
}
