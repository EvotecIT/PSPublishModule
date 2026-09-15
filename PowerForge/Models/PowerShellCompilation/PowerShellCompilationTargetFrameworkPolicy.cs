namespace PowerForge;

/// <summary>Canonical generated-artifact target framework policy for PowerShell compilation.</summary>
internal static class PowerShellCompilationTargetFrameworkPolicy
{
    internal const string Default = Modern;
    internal const string Legacy = "net472";
    internal const string Modern = "net10.0";

    internal static bool IsSupported(string? targetFramework)
        => targetFramework?.Equals(Legacy, StringComparison.OrdinalIgnoreCase) == true ||
           targetFramework?.Equals(Modern, StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsModern(string? targetFramework)
        => targetFramework?.Equals(Modern, StringComparison.OrdinalIgnoreCase) == true;

    internal static void EnsureSupported(string? targetFramework, PowerShellCompilationArtifactKind? kind = null)
    {
        if (kind == PowerShellCompilationArtifactKind.Executable)
        {
            if (IsModern(targetFramework)) return;
            throw new ArgumentException($"PowerShell compilation executables currently target {Modern}.", nameof(targetFramework));
        }
        if (IsSupported(targetFramework)) return;
        throw new ArgumentException(
            $"PowerShell compilation libraries and binary modules currently target {Legacy} or {Modern}.",
            nameof(targetFramework));
    }
}
