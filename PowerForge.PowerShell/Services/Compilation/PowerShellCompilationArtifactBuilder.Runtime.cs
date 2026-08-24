using System.Runtime.InteropServices;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    internal static string GetExecutableFileName(string artifactName, string? runtimeIdentifier)
    {
        var targetsWindows = string.IsNullOrWhiteSpace(runtimeIdentifier)
            ? RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            : runtimeIdentifier!.StartsWith("win", StringComparison.OrdinalIgnoreCase);
        return targetsWindows ? artifactName + ".exe" : artifactName;
    }

    internal static string GetDefaultRuntimeIdentifier(
        bool isWindows,
        bool isMacOS,
        string? hostRuntimeIdentifier,
        Architecture architecture)
    {
        var architectureName = architecture switch
        {
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64"
        };
        if (isWindows)
            return "win-" + architectureName;
        if (isMacOS)
            return "osx-" + architectureName;
        if (string.IsNullOrWhiteSpace(hostRuntimeIdentifier))
            throw new InvalidOperationException("The active Linux libc could not be determined. Specify RuntimeIdentifier explicitly for single-file or self-contained publication.");
        var prefix = hostRuntimeIdentifier!.Contains("musl", StringComparison.OrdinalIgnoreCase)
            ? "linux-musl"
            : "linux";
        return prefix + "-" + architectureName;
    }

    private static string? ResolveRuntimeIdentifier(PowerShellCompilationBuildSpec spec)
    {
        if (spec.Kind != PowerShellCompilationArtifactKind.Executable)
            return null;
        if (!string.IsNullOrWhiteSpace(spec.RuntimeIdentifier))
            return spec.RuntimeIdentifier;
        if (!spec.SingleFile && !spec.SelfContained)
            return null;
        return GetDefaultRuntimeIdentifier(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
            GetHostRuntimeIdentifier(),
            RuntimeInformation.ProcessArchitecture);
    }

    private static string? GetHostRuntimeIdentifier()
    {
#if NETFRAMEWORK
        return null;
#else
        return RuntimeInformation.RuntimeIdentifier;
#endif
    }
}
