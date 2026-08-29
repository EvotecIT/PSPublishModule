using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

/// <summary>Converts a completed script-module staging tree into a generated binary-module staging tree.</summary>
internal sealed class PowerShellModuleCompilationIntegrator
{
    private const int CheckpointSchemaVersion = 1;

    public (ModuleBuildResult BuildResult, PowerShellModuleCompilationResult CompilationResult) Compile(
        ModuleBuildResult buildResult,
        PowerShellModuleCompilationConfiguration configuration)
    {
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (!configuration.Enabled)
            throw new InvalidOperationException("PowerShell module compilation is not enabled.");

        var stagingPath = Path.GetFullPath(buildResult.StagingPath);
        var moduleName = Path.GetFileNameWithoutExtension(buildResult.ManifestPath);
        var workingRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "module-compilation", Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(workingRoot, "output");
        var candidatePath = stagingPath + ".compiled-" + Guid.NewGuid().ToString("N");
        var backupPath = stagingPath + ".script-backup-" + Guid.NewGuid().ToString("N");

        try
        {
            var resolved = new PowerShellCompilationInputResolver().Resolve(
                stagingPath,
                PowerShellCompilationArtifactKind.BinaryModule,
                configuration.Mode);
            var compilationSpec = new PowerShellCompilationBuildSpec(
                resolved.SourcePath,
                outputRoot,
                moduleName,
                PowerShellCompilationArtifactKind.BinaryModule,
                configuration.Mode)
            {
                ModuleManifestPath = resolved.ModuleManifestPath,
                CompilationSourcePaths = resolved.CompilationSourceFiles,
                RuntimeSourcePaths = resolved.SourceFiles,
                ResourceMode = configuration.ResourceMode,
                IncludeResource = configuration.IncludeResource ?? Array.Empty<string>(),
                ExcludeResource = configuration.ExcludeResource ?? Array.Empty<string>(),
                TargetFramework = configuration.TargetFramework,
                UseBuildCache = configuration.UseBuildCache,
                BuildCacheDirectory = configuration.BuildCacheDirectory,
                ExpectedDependencyLock = configuration.DependencyLock,
                AllowUnreviewedDependencyResolution = configuration.AllowUnreviewedDependencies,
                TimeoutSeconds = configuration.TimeoutSeconds
            };

            var compiled = new PowerShellCompilationArtifactBuilder().Build(compilationSpec);
            if (!compiled.Succeeded || compiled.Manifest is null || string.IsNullOrWhiteSpace(compiled.ArtifactPath))
                throw new InvalidOperationException("PowerShell module compilation failed. " + (compiled.Error ?? "No artifact was produced."));

            var compiledModulePath = Path.GetDirectoryName(compiled.ArtifactPath)
                ?? throw new InvalidOperationException("The generated binary module has no containing directory.");
            CopyTree(compiledModulePath, candidatePath, static _ => true, overwrite: true);
            var candidateManifest = Path.Combine(candidatePath, moduleName + ".psd1");
            var candidateAssembly = Path.Combine(candidatePath, moduleName + ".dll");
            if (!File.Exists(candidateManifest) || !File.Exists(candidateAssembly))
                throw new InvalidOperationException("The generated binary module did not contain its expected manifest and assembly.");
            var exports = ModuleManifestExportReader.ReadExports(candidateManifest);

            ReplaceDirectory(stagingPath, candidatePath, backupPath);
            var stagedManifest = Path.Combine(stagingPath, moduleName + ".psd1");
            var stagedAssembly = Path.Combine(stagingPath, moduleName + ".dll");

            var manifest = compiled.Manifest;
            var totalUnits = manifest.CompiledMethods + manifest.RuntimeFallbackUnits + manifest.OmittedUnits;
            var result = new PowerShellModuleCompilationResult
            {
                Mode = configuration.Mode,
                TargetFramework = configuration.TargetFramework,
                TotalUnits = totalUnits,
                CompiledUnits = manifest.CompiledMethods,
                RuntimeFallbackUnits = manifest.RuntimeFallbackUnits,
                CoveragePercentage = manifest.CompilationCoveragePercentage,
                UsesPowerShellRuntimeFallback = manifest.UsesPowerShellRuntimeFallback,
                AssemblyPath = stagedAssembly,
                ModuleManifestPath = stagedManifest
            };
            return (new ModuleBuildResult(stagingPath, stagedManifest, exports, buildResult.BuildNotes), result);
        }
        finally
        {
            TryDeleteDirectory(candidatePath);
            TryDeleteDirectory(backupPath);
            TryDeleteDirectory(workingRoot);
        }
    }

    public (ModuleBuildResult BuildResult, PowerShellModuleCompilationResult CompilationResult) Restore(
        ModuleBuildResult buildResult,
        PowerShellModuleCompilationConfiguration configuration)
    {
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var stagingPath = Path.GetFullPath(buildResult.StagingPath);
        var moduleName = Path.GetFileNameWithoutExtension(buildResult.ManifestPath);
        var checkpointPath = GetCheckpointPath(stagingPath, moduleName);
        if (!File.Exists(checkpointPath))
            throw new InvalidOperationException($"Reusable compiled staging is missing its compilation checkpoint: '{checkpointPath}'.");

        var checkpoint = JsonSerializer.Deserialize<CompilationCheckpoint>(File.ReadAllText(checkpointPath))
                         ?? throw new InvalidOperationException("Reusable compiled staging contains an unreadable compilation checkpoint.");
        if (checkpoint.SchemaVersion != CheckpointSchemaVersion ||
            !checkpoint.ArtifactName.Equals(moduleName, StringComparison.Ordinal) ||
            checkpoint.Mode != configuration.Mode ||
            !checkpoint.TargetFramework.Equals(configuration.TargetFramework, StringComparison.Ordinal) ||
            !checkpoint.AssemblyFileName.Equals(moduleName + ".dll", StringComparison.Ordinal) ||
            checkpoint.ResourceMode != configuration.ResourceMode ||
            !(checkpoint.IncludeResource ?? Array.Empty<string>()).SequenceEqual(NormalizePatterns(configuration.IncludeResource), StringComparer.Ordinal) ||
            !(checkpoint.ExcludeResource ?? Array.Empty<string>()).SequenceEqual(NormalizePatterns(configuration.ExcludeResource), StringComparer.Ordinal) ||
            !checkpoint.DependencyLockSha256.Equals(configuration.DependencyLock?.LockSha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            checkpoint.AllowUnreviewedDependencies != configuration.AllowUnreviewedDependencies)
        {
            throw new InvalidOperationException("Reusable compiled staging does not match the requested PowerShell compilation contract.");
        }

        var assemblyPath = Path.Combine(stagingPath, checkpoint.AssemblyFileName);
        if (!File.Exists(assemblyPath) || !ComputeSha256(assemblyPath).Equals(checkpoint.AssemblySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Reusable compiled staging assembly does not match its compilation checkpoint.");
        if (!File.Exists(buildResult.ManifestPath))
            throw new InvalidOperationException("Reusable compiled staging is missing its module manifest.");
        var actualFiles = BuildFileInventory(stagingPath, checkpointPath);
        if (!InventoryMatches(checkpoint.Files ?? Array.Empty<CheckpointFile>(), actualFiles))
            throw new InvalidOperationException("Reusable compiled staging payload does not match its compilation checkpoint.");

        var result = new PowerShellModuleCompilationResult
        {
            Mode = checkpoint.Mode,
            TargetFramework = checkpoint.TargetFramework,
            TotalUnits = checkpoint.TotalUnits,
            CompiledUnits = checkpoint.CompiledUnits,
            RuntimeFallbackUnits = checkpoint.RuntimeFallbackUnits,
            CoveragePercentage = checkpoint.CoveragePercentage,
            UsesPowerShellRuntimeFallback = checkpoint.UsesPowerShellRuntimeFallback,
            AssemblyPath = assemblyPath,
            ModuleManifestPath = buildResult.ManifestPath
        };
        var exports = ModuleManifestExportReader.ReadExports(buildResult.ManifestPath);
        return (new ModuleBuildResult(stagingPath, buildResult.ManifestPath, exports, buildResult.BuildNotes), result);
    }

    public void PersistCheckpoint(
        PowerShellModuleCompilationResult result,
        PowerShellModuleCompilationConfiguration configuration)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (!File.Exists(result.AssemblyPath))
            throw new FileNotFoundException("Compiled module assembly was not found while writing its checkpoint.", result.AssemblyPath);

        var stagingPath = Path.GetDirectoryName(Path.GetFullPath(result.ModuleManifestPath))
                          ?? throw new InvalidOperationException("Compiled module manifest has no containing directory.");
        var moduleName = Path.GetFileNameWithoutExtension(result.ModuleManifestPath);
        var checkpointPath = GetCheckpointPath(stagingPath, moduleName);
        var checkpoint = new CompilationCheckpoint
        {
            SchemaVersion = CheckpointSchemaVersion,
            ArtifactName = moduleName,
            Mode = result.Mode,
            TargetFramework = result.TargetFramework,
            TotalUnits = result.TotalUnits,
            CompiledUnits = result.CompiledUnits,
            RuntimeFallbackUnits = result.RuntimeFallbackUnits,
            CoveragePercentage = result.CoveragePercentage,
            UsesPowerShellRuntimeFallback = result.UsesPowerShellRuntimeFallback,
            AssemblyFileName = Path.GetFileName(result.AssemblyPath),
            AssemblySha256 = ComputeSha256(result.AssemblyPath),
            ResourceMode = configuration.ResourceMode,
            IncludeResource = NormalizePatterns(configuration.IncludeResource),
            ExcludeResource = NormalizePatterns(configuration.ExcludeResource),
            DependencyLockSha256 = configuration.DependencyLock?.LockSha256 ?? string.Empty,
            AllowUnreviewedDependencies = configuration.AllowUnreviewedDependencies,
            Files = BuildFileInventory(stagingPath, checkpointPath)
        };
        File.WriteAllText(
            checkpointPath,
            JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CopyTree(string sourceRoot, string destinationRoot, Func<string, bool> includeFile, bool overwrite)
    {
        EnsureNotReparsePoint(sourceRoot);
        Directory.CreateDirectory(destinationRoot);
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((sourceRoot, destinationRoot));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current.Source, "*", SearchOption.TopDirectoryOnly))
            {
                EnsureNotReparsePoint(entry);
                var destination = Path.Combine(current.Destination, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    Directory.CreateDirectory(destination);
                    pending.Push((entry, destination));
                    continue;
                }

                if (!includeFile(entry)) continue;
                File.Copy(entry, destination, overwrite);
            }
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Module compilation staging does not copy symbolic links or junctions: '{path}'.");
    }

    private static void ReplaceDirectory(string stagingPath, string candidatePath, string backupPath)
    {
        Directory.Move(stagingPath, backupPath);
        try
        {
            Directory.Move(candidatePath, stagingPath);
        }
        catch
        {
            if (!Directory.Exists(stagingPath) && Directory.Exists(backupPath))
                Directory.Move(backupPath, stagingPath);
            throw;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }

    private static string GetCheckpointPath(string stagingPath, string moduleName)
        => Path.Combine(stagingPath, moduleName + ".powerforge-module-compilation.json");

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream)
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string[] NormalizePatterns(IEnumerable<string>? patterns)
        => (patterns ?? Array.Empty<string>())
            .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(static pattern => pattern.Trim().Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static pattern => pattern, StringComparer.Ordinal)
            .ToArray();

    private static CheckpointFile[] BuildFileInventory(string stagingPath, string checkpointPath)
    {
        EnsureNotReparsePoint(stagingPath);
        var files = new List<CheckpointFile>();
        var pending = new Stack<string>();
        pending.Push(stagingPath);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly))
            {
                EnsureNotReparsePoint(entry);
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                    continue;
                }
                if (PowerShellCompilationPathSafety.PathEquals(entry, checkpointPath)) continue;
                files.Add(new CheckpointFile
                {
                    Path = FrameworkCompatibility.GetRelativePath(stagingPath, entry).Replace('\\', '/'),
                    SizeBytes = new FileInfo(entry).Length,
                    Sha256 = ComputeSha256(entry)
                });
            }
        }
        return files.OrderBy(static file => file.Path, StringComparer.Ordinal).ToArray();
    }

    private static bool InventoryMatches(CheckpointFile[] expected, CheckpointFile[] actual)
    {
        if (expected.Length != actual.Length) return false;
        for (var index = 0; index < expected.Length; index++)
        {
            if (!expected[index].Path.Equals(actual[index].Path, StringComparison.Ordinal) ||
                expected[index].SizeBytes != actual[index].SizeBytes ||
                !expected[index].Sha256.Equals(actual[index].Sha256, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private sealed class CompilationCheckpoint
    {
        public int SchemaVersion { get; set; }
        public string ArtifactName { get; set; } = string.Empty;
        public PowerShellCompilationMode Mode { get; set; }
        public string TargetFramework { get; set; } = string.Empty;
        public int TotalUnits { get; set; }
        public int CompiledUnits { get; set; }
        public int RuntimeFallbackUnits { get; set; }
        public double CoveragePercentage { get; set; }
        public bool UsesPowerShellRuntimeFallback { get; set; }
        public string AssemblyFileName { get; set; } = string.Empty;
        public string AssemblySha256 { get; set; } = string.Empty;
        public PowerShellCompilationResourceMode ResourceMode { get; set; }
        public string[] IncludeResource { get; set; } = Array.Empty<string>();
        public string[] ExcludeResource { get; set; } = Array.Empty<string>();
        public string DependencyLockSha256 { get; set; } = string.Empty;
        public bool AllowUnreviewedDependencies { get; set; }
        public CheckpointFile[] Files { get; set; } = Array.Empty<CheckpointFile>();
    }

    private sealed class CheckpointFile
    {
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}
