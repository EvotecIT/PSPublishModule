namespace PowerForge;

public sealed partial class PowerShellCompilationAnalyzer
{
    /// <summary>Analyzes a PowerShell file or directory.</summary>
    public PowerShellCompilationPlan Analyze(PowerShellCompilationSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var files = DiscoverFiles(spec);
        var basePath = Directory.Exists(spec.Path) ? spec.Path : Path.GetDirectoryName(spec.Path) ?? Directory.GetCurrentDirectory();
        return AnalyzeFiles(spec.Mode, files, basePath, spec.TargetFramework, spec.Capabilities);
    }

    internal PowerShellCompilationPlan AnalyzeFiles(
        PowerShellCompilationMode mode,
        IEnumerable<string> sourcePaths,
        string basePath,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var files = sourcePaths.Select(Path.GetFullPath).Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        var analysisTargetFramework = mode == PowerShellCompilationMode.Package ? null : targetFramework;
        if (analysisTargetFramework is not null)
        {
            PowerShellGeneratedTargetFrameworkPolicy.EnsureHostCanAnalyze(analysisTargetFramework);
            PowerShellGeneratedReferenceAssemblyResolver.EnsureAvailable(analysisTargetFramework);
        }
        var localFunctionNames = capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls)
            ? PowerShellLocalFunctionDiscovery.DiscoverNames(files)
            : null;
        return new PowerShellCompilationPlan(
            mode,
            files.Select(file => AnalyzeFile(file, basePath, analysisTargetFramework, capabilities, localFunctionNames)).ToArray(),
            targetFramework);
    }

    private static string[] DiscoverFiles(PowerShellCompilationSpec spec)
    {
        if (File.Exists(spec.Path))
        {
            var extension = Path.GetExtension(spec.Path);
            if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("PowerShell compilation accepts .ps1 and .psm1 files.", nameof(spec));
            return new[] { spec.Path };
        }

        if (!Directory.Exists(spec.Path))
            throw new DirectoryNotFoundException($"PowerShell compilation input was not found: {spec.Path}");

        return EnumerateSourceFiles(spec.Path, spec.Recurse, spec.ExcludeDirectories)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root, bool recurse, string[] exclusions)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }

            if (!recurse) continue;
            foreach (var directory in Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (IsExcludedDirectory(name, exclusions)) continue;
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(directory);
            }
        }
    }

    private static bool IsExcludedDirectory(string directory, string[] exclusions)
        => exclusions.Any(exclusion =>
            directory.Equals(exclusion, StringComparison.OrdinalIgnoreCase) ||
            ((exclusion.Equals("bin", StringComparison.OrdinalIgnoreCase) || exclusion.Equals("obj", StringComparison.OrdinalIgnoreCase)) &&
             directory.StartsWith(exclusion + "-", StringComparison.OrdinalIgnoreCase)));
}
