using System.Management.Automation;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static void ValidateSpec(PowerShellCompilationBuildSpec spec)
    {
        if (!Enum.IsDefined(typeof(PowerShellCompilationArtifactKind), spec.Kind))
            throw new ArgumentOutOfRangeException(nameof(spec), "Artifact kind is not defined.");
        if (!Enum.IsDefined(typeof(PowerShellCompilationMode), spec.Mode))
            throw new ArgumentOutOfRangeException(nameof(spec), "Compilation mode is not defined.");
        if (!Enum.IsDefined(typeof(PowerShellCompilationExecutableOptimization), spec.Optimization))
            throw new ArgumentOutOfRangeException(nameof(spec), "Executable optimization is not defined.");
        if (!Enum.IsDefined(typeof(PowerShellCompilationResourceMode), spec.ResourceMode))
            throw new ArgumentOutOfRangeException(nameof(spec), "Resource mode is not defined.");
        if (!Enum.IsDefined(typeof(CertificateStoreLocation), spec.CertificateStoreLocation))
            throw new ArgumentOutOfRangeException(nameof(spec), "Certificate store location is not defined.");
        if (spec.ExpectedDependencyLock is not null && spec.AllowUnreviewedDependencyResolution)
            throw new ArgumentException("ExpectedDependencyLock and AllowUnreviewedDependencyResolution are mutually exclusive.", nameof(spec));
        var expectedPublicAbiSha256 = spec.ExpectedPublicAbiSha256;
        if (expectedPublicAbiSha256 is not null && !string.IsNullOrWhiteSpace(expectedPublicAbiSha256) &&
            (expectedPublicAbiSha256.Length != 64 || expectedPublicAbiSha256.Any(static character => !Uri.IsHexDigit(character))))
            throw new ArgumentException("ExpectedPublicAbiSha256 must be a 64-character hexadecimal SHA-256 value.", nameof(spec));
        if (!string.IsNullOrWhiteSpace(expectedPublicAbiSha256) && spec.Mode != PowerShellCompilationMode.Strict)
            throw new ArgumentException("ExpectedPublicAbiSha256 is supported only for Strict compilation.", nameof(spec));
        if (spec.EmitIrSnapshots && spec.Mode == PowerShellCompilationMode.Package)
            throw new ArgumentException("IR snapshots require Hybrid or Strict compilation.", nameof(spec));
        if (spec.Optimization == PowerShellCompilationExecutableOptimization.NativeAot &&
            spec.CommandProviders.Any(static provider => provider.Adapter.RuntimeFree && !provider.Adapter.AotCompatible))
            throw new ArgumentException("NativeAOT compilation requires every runtime-free command provider adapter to declare AotCompatible.", nameof(spec));
        if (spec.Mode == PowerShellCompilationMode.Analyze)
            throw new ArgumentException("Analyze mode reports eligibility and does not produce artifacts. Use the analyzer API or CLI analyze command.", nameof(spec));
        if (!File.Exists(spec.SourcePath))
            throw new FileNotFoundException("PowerShell source file was not found.", spec.SourcePath);
        var extension = Path.GetExtension(spec.SourcePath);
        if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PowerShell artifacts accept .ps1 and .psm1 source files.", nameof(spec));
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable &&
            !extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Executable compilation requires a standalone .ps1 entrypoint; a .psm1 module has no unambiguous application entrypoint.", nameof(spec));
        var effectiveManifestPath = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
            ? PowerShellCompiledModuleManifest.ResolveSourceManifest(spec.SourcePath, spec.ModuleManifestPath)
            : spec.ModuleManifestPath;
        if (!string.IsNullOrWhiteSpace(effectiveManifestPath) &&
            (!string.IsNullOrWhiteSpace(spec.ModuleManifestPath) || File.Exists(effectiveManifestPath)))
        {
            var moduleManifestPath = effectiveManifestPath!;
            if (!File.Exists(moduleManifestPath))
                throw new FileNotFoundException("PowerShell module manifest was not found.", moduleManifestPath);
            if (!Path.GetExtension(moduleManifestPath).Equals(".psd1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("ModuleManifestPath must reference a .psd1 file.", nameof(spec));
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath));
            var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(moduleManifestPath));
            if (!PowerShellCompilationPathSafety.PathEquals(sourceDirectory, manifestDirectory))
                throw new ArgumentException("The source .psm1 and module manifest must reside in the same module directory.", nameof(spec));
            PowerShellCompiledModuleManifest.EnsureManifestOwnsSource(spec.SourcePath, moduleManifestPath);
            PowerShellCompilationPathSafety.EnsureNoLinks(
                sourceDirectory!,
                Path.GetFullPath(moduleManifestPath),
                $"PowerShell module manifest '{moduleManifestPath}' traverses a symbolic link or junction.");
        }
        if (spec.TimeoutSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(spec), "Build timeout must be positive.");
        if (!string.IsNullOrWhiteSpace(spec.NuGetLockFilePath))
        {
            var nuGetLockPath = Path.GetFullPath(spec.NuGetLockFilePath!.Trim().Trim('"'));
            if (!File.Exists(nuGetLockPath))
                throw new FileNotFoundException("The exact NuGet closure lock was not found.", nuGetLockPath);
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(
                nuGetLockPath,
                "The exact NuGet closure lock traverses a symbolic link or junction.");
        }
        if (spec.SignArtifact && string.IsNullOrWhiteSpace(spec.TimeStampServer))
            throw new ArgumentException("Signing requires an RFC3161 timestamp server URL.", nameof(spec));
        if (spec.SigningTimeoutSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(spec), "Signing timeout must be positive.");
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && !spec.TargetFramework.Equals("net8.0", StringComparison.OrdinalIgnoreCase) && !spec.TargetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Executables currently target net8.0 or net10.0.", nameof(spec));
        PowerShellCompilationBuildSpec.EnsureModeSupported(spec.Kind, spec.Mode);
        if (spec.Kind != PowerShellCompilationArtifactKind.Executable &&
            (spec.SelfContained || !string.IsNullOrWhiteSpace(spec.RuntimeIdentifier)))
            throw new ArgumentException("SelfContained and RuntimeIdentifier are executable-only publication options.", nameof(spec));
        if (spec.Optimization != PowerShellCompilationExecutableOptimization.None)
        {
            if (spec.Kind != PowerShellCompilationArtifactKind.Executable || spec.Mode != PowerShellCompilationMode.Strict)
                throw new ArgumentException("Executable optimization is supported only for Strict genuinely typed executables.", nameof(spec));
            if (!spec.SelfContained || string.IsNullOrWhiteSpace(spec.RuntimeIdentifier) || !spec.SingleFile)
                throw new ArgumentException("Trimmed and NativeAot executables require SelfContained, RuntimeIdentifier, and SingleFile.", nameof(spec));
        }
        if (spec.Kind != PowerShellCompilationArtifactKind.Executable &&
            !spec.TargetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase) &&
            !spec.TargetFramework.Equals("net8.0", StringComparison.OrdinalIgnoreCase) &&
            !spec.TargetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Typed libraries and binary modules currently target net472, net8.0, or net10.0.", nameof(spec));
    }

    private static string[] ResolveCompilationSourcePaths(PowerShellCompilationBuildSpec spec)
    {
        var sourcePath = Path.GetFullPath(spec.SourcePath);
        var sourceRoot = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var paths = new[] { sourcePath }
            .Concat(spec.CompilationSourcePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim().Trim('"')))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("PowerShell compilation source file was not found.", path);
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"PowerShell compilation source '{path}' must be a .ps1 or .psm1 file.", nameof(spec));
            if (!PowerShellCompilationPathSafety.PathEquals(path, sourcePath))
                PowerShellCompilationPathSafety.EnsureContained(sourceRoot, path, $"Additional compilation source '{path}' escapes the root module directory.");
            PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, path, $"Compilation source '{path}' traverses a symbolic link or junction.");
        }
        return paths;
    }

    private static PowerShellCompilationPlan AnalyzeCompilationSources(
        IEnumerable<string> sourcePaths,
        PowerShellCompilationMode mode,
        string targetFramework,
        string semanticProfileId,
        PowerShellCompilationCapability capabilities,
        IEnumerable<PowerShellCompilationCommandProviderContract> commandProviders)
    {
        var analyzer = new PowerShellCompilationAnalyzer(commandProviders, semanticProfileId);
        var paths = sourcePaths.Select(Path.GetFullPath).Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        var basePath = paths.Length == 0
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(paths[0]) ?? Directory.GetCurrentDirectory();
        return analyzer.AnalyzeFiles(mode, paths, basePath, targetFramework, capabilities);
    }

    private static void ValidateRuntimeHookSourceOwnership(
        PowerShellCompilationBuildSpec spec,
        IEnumerable<string> compilationSourcePaths)
    {
        if (spec.Kind != PowerShellCompilationArtifactKind.BinaryModule)
            return;
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        var compilationSources = compilationSourcePaths.Select(Path.GetFullPath).ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var overlap = PowerShellCompiledModuleManifest.GetContainedRuntimeScriptFiles(spec.SourcePath, spec.ModuleManifestPath)
            .Select(reference => Path.GetFullPath(Path.Combine(
                sourceRoot,
                PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(reference))))
            .FirstOrDefault(compilationSources.Contains);
        if (overlap is not null)
        {
            throw new InvalidOperationException(
                $"PowerShell source '{overlap}' cannot be both an explicit compilation source and a manifest runtime hook. Remove it from CompilationSourcePaths so its runtime scope and loading semantics are preserved.");
        }
    }

    private static void ValidateRuntimeSourcePaths(
        PowerShellCompilationBuildSpec spec,
        IEnumerable<string> compilationSourcePaths)
    {
        if (spec.RuntimeSourcePaths is not { Length: > 0 }) return;
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        var compiled = compilationSourcePaths.Select(Path.GetFullPath).ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        foreach (var runtimeSource in spec.RuntimeSourcePaths
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Select(path => Path.GetFullPath(path.Trim().Trim('"')))
                     .Distinct(PowerShellCompilationPathSafety.PathComparer))
        {
            if (!File.Exists(runtimeSource))
                throw new FileNotFoundException("PowerShell runtime source file was not found.", runtimeSource);
            var extension = Path.GetExtension(runtimeSource);
            if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"PowerShell runtime source '{runtimeSource}' must be a .ps1 or .psm1 file.", nameof(spec));
            if (!compiled.Contains(runtimeSource))
                PowerShellCompilationPathSafety.EnsureContained(sourceRoot, runtimeSource, $"Runtime source '{runtimeSource}' escapes the root module directory.");
            PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, runtimeSource, $"Runtime source '{runtimeSource}' traverses a symbolic link or junction.");
        }
    }
}
