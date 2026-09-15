using System.Diagnostics;

namespace PowerForge;

/// <summary>Applies deterministic lifetime settings to captured .NET CLI processes.</summary>
internal static class DotNetProcessLifetime
{
    internal const string DisableNodeReuseEnvironmentVariable = "MSBUILDDISABLENODEREUSE";

    internal static void DisableBuildServerReuse(ProcessStartInfo startInfo)
    {
        if (startInfo is null)
            throw new ArgumentNullException(nameof(startInfo));

        var executable = startInfo.FileName?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(executable) ||
            !string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        startInfo.EnvironmentVariables[DisableNodeReuseEnvironmentVariable] = "1";
    }
}
