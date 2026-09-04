using System.Runtime.InteropServices;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal const long DeterministicPackageEpochSeconds = 946684800;

    internal static void EnsureNativeInstallerOutputDoesNotOverlapSource(
        string sourceRoot,
        string installerOutputPath,
        string installerId)
    {
        string source = NormalizeDirectory(sourceRoot);
        string output = NormalizeDirectory(Path.GetDirectoryName(Path.GetFullPath(installerOutputPath))!);
        StringComparison comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(source, output, comparison) ||
            output.StartsWith(source + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException(
                $"Native installer '{installerId}' output directory must not overlap its published payload directory.");
        }
    }

    private static string NormalizeDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
