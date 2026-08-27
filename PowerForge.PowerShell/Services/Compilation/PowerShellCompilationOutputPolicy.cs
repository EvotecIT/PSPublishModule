namespace PowerForge;

/// <summary>
/// Selects and validates durable output locations for PowerShell compilation artifacts.
/// </summary>
public static class PowerShellCompilationOutputPolicy
{
    /// <summary>
    /// Returns a default output directory that cannot be rediscovered by an authored recursive module loader.
    /// </summary>
    public static string GetDefaultOutputDirectory(PowerShellCompilationResolvedInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var candidate = Path.Combine(input.ModuleRoot, "artifacts");
        if (!input.RecursiveSourceDirectories.Any(root => IsSameOrContained(root, candidate)))
            return candidate;

        var parent = Directory.GetParent(input.ModuleRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidOperationException(
                $"A safe default output directory cannot be selected outside recursive loader root '{input.ModuleRoot}'. Specify OutputDirectory explicitly.");
        }
        return Path.Combine(parent!, "artifacts", new DirectoryInfo(input.ModuleRoot).Name);
    }

    internal static void EnsureDoesNotOverlapRecursiveLoaderRoot(string sourcePath, string outputDirectory)
    {
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();
        if (!IsSameOrContained(sourceRoot, outputDirectory))
            return;

        var discovery = PowerShellConventionalModuleSourceDiscovery.Analyze(sourcePath);
        var overlap = discovery.RecursiveSourceDirectories.FirstOrDefault(root => IsSameOrContained(root, outputDirectory));
        if (overlap is null)
            return;

        throw new InvalidOperationException(
            $"PowerShell compilation output directory '{Path.GetFullPath(outputDirectory)}' is inside recursive conventional loader root '{overlap}'. Choose an output directory outside that loader root so generated scripts cannot be rediscovered as authored source.");
    }

    private static bool IsSameOrContained(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return PowerShellCompilationPathSafety.PathEquals(fullRoot, fullPath) ||
               PowerShellCompilationPathSafety.PathStartsWith(fullPath, fullRoot + Path.DirectorySeparatorChar);
    }
}
