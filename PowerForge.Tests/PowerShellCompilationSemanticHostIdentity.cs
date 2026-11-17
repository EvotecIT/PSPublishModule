using System.Runtime.InteropServices;
using PowerForge;

namespace PowerForge.Tests;

internal static class PowerShellCompilationSemanticTestHost
{
    internal static IReadOnlyDictionary<string, string> ExpectedArtifacts
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId] = GetIdentity(
                PowerShellCompilationSemanticHostArtifactPinCatalog.Get(
                    PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId))!
        };

    // The release archive pins PowerShell 7.6, not the OS image running it.
    // Windows PowerShell is serviced with Windows and is checked as the installed
    // supported profile; its full observed identity remains in every observation.
    internal static string? GetIdentity(PowerShellCompilationSemanticHostArtifactPin pin)
    {
        if (pin.ProfileId == PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)
            return null;

        var artifact = pin.GetHostArtifact();
        artifact.OperatingSystemVersion = RuntimeInformation.OSDescription;
        artifact.IdentitySha256 = string.Empty;
        return PowerShellCompilationSemanticHostArtifactService.Normalize(artifact).IdentitySha256;
    }
}
