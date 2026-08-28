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
        var architectureName = architecture.ToString().ToUpperInvariant() switch
        {
            "X86" => "x86",
            "X64" => "x64",
            "ARM" => "arm",
            "ARM64" => "arm64",
            "ARMV6" => "armv6",
            "S390X" => "s390x",
            "PPC64LE" => "ppc64le",
            "LOONGARCH64" => "loongarch64",
            _ => throw new InvalidOperationException(
                $"The active process architecture '{architecture}' has no default .NET runtime identifier mapping. Specify RuntimeIdentifier explicitly.")
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

    internal static string? ResolveRuntimeIdentifier(PowerShellCompilationBuildSpec spec)
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

    internal static string? GetHostRuntimeIdentifier()
    {
#if NETFRAMEWORK
        return null;
#else
        return RuntimeInformation.RuntimeIdentifier;
#endif
    }
}
