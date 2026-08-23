using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Limits typed compilation to CLR types supplied by the generated project's shared framework.
/// </summary>
internal static class PowerShellGeneratedTypePolicy
{
    internal static bool IsSupported(Type type)
    {
        if (type.IsArray)
            return type.GetArrayRank() == 1 && IsSupported(type.GetElementType()!);
        if (type.IsGenericType || type.IsByRef || type.IsPointer)
            return false;
        var location = type.Assembly.Location;
        if (string.IsNullOrWhiteSpace(location))
            return type.Assembly == typeof(object).Assembly;
        var runtimeDirectory = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(location).StartsWith(
            runtimeDirectory,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
