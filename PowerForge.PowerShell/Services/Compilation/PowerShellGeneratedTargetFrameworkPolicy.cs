using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Prevents target analysis from borrowing a member inventory from an older host runtime.
/// </summary>
internal static class PowerShellGeneratedTargetFrameworkPolicy
{
    internal static void EnsureHostCanAnalyze(string? targetFramework)
    {
        var framework = targetFramework?.Trim();
        if (framework is not null && framework.Length > 0)
            PowerShellCompilationTargetFrameworkPolicy.EnsureSupported(framework);
        if (IsHostCompatible(
                framework,
                Environment.Version.Major,
                RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase)))
            return;

        throw new InvalidOperationException(
            $"Target framework '{framework}' requires a .NET {GetRequiredModernHostMajor(framework!)} or newer host for accurate CLR member analysis. " +
            "Run the matching PowerForge CLI/module target or choose a target framework no newer than the current host.");
    }

    internal static bool IsHostCompatible(string? targetFramework, int hostMajor, bool isNetFrameworkHost)
    {
        if (targetFramework is null)
            return true;

        var framework = targetFramework.Trim();
        if (framework.Length == 0 || framework.Equals(PowerShellCompilationTargetFrameworkPolicy.Legacy, StringComparison.OrdinalIgnoreCase))
            return true;
        return PowerShellCompilationTargetFrameworkPolicy.IsModern(framework) && !isNetFrameworkHost && hostMajor >= 10;
    }

    private static int GetRequiredModernHostMajor(string targetFramework)
        => PowerShellCompilationTargetFrameworkPolicy.IsModern(targetFramework) ? 10 : 0;
}
