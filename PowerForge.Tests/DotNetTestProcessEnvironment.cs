using System.Diagnostics;

namespace PowerForge.Tests;

internal static class DotNetTestProcessEnvironment
{
    internal static void DisableBuildServers(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
    }
}
