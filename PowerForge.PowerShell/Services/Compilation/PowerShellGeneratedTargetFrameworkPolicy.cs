using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Prevents target analysis from borrowing a member inventory from an older host runtime.
/// </summary>
internal static class PowerShellGeneratedTargetFrameworkPolicy
{
    internal static void EnsureHostCanAnalyze(string? targetFramework)
    {
        if (!string.IsNullOrWhiteSpace(targetFramework))
            PowerShellCompilationTargetFrameworkPolicy.EnsureSupported(targetFramework);
        if (IsHostCompatible(
                targetFramework,
                Environment.Version.Major,
                RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase)))
            return;

        throw new InvalidOperationException(
            $"Target framework '{targetFramework}' requires a .NET {GetRequiredModernHostMajor(targetFramework!)} or newer host for accurate CLR member analysis. " +
            "Run the matching PowerForge CLI/module target or choose a target framework no newer than the current host.");
    }

    internal static bool IsHostCompatible(string? targetFramework, int hostMajor, bool isNetFrameworkHost)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
            return true;
        if (targetFramework.Equals(PowerShellCompilationTargetFrameworkPolicy.Legacy, StringComparison.OrdinalIgnoreCase))
            return true;
        return PowerShellCompilationTargetFrameworkPolicy.IsModern(targetFramework) && !isNetFrameworkHost && hostMajor >= 10;
    }

    private static int GetRequiredModernHostMajor(string targetFramework)
        => PowerShellCompilationTargetFrameworkPolicy.IsModern(targetFramework) ? 10 : 0;
}
