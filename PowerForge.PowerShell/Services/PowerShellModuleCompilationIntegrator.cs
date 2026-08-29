using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Converts a completed script-module staging tree into a generated binary-module staging tree.</summary>
internal sealed class PowerShellModuleCompilationIntegrator
{
    private const int CheckpointSchemaVersion = 3;

    public (ModuleBuildResult BuildResult, PowerShellModuleCompilationResult CompilationResult) Compile(
        ModuleBuildResult buildResult,
        PowerShellModuleCompilationConfiguration configuration,
        PowerShellModuleCompilationReleaseContract releaseContract)
    {
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (releaseContract is null) throw new ArgumentNullException(nameof(releaseContract));
        if (!configuration.Enabled)
            throw new InvalidOperationException("PowerShell module compilation is not enabled.");
        ValidateDependencyLock(configuration.DependencyLock);

        var stagingPath = Path.GetFullPath(buildResult.StagingPath);
        var moduleName = Path.GetFileNameWithoutExtension(buildResult.ManifestPath);
        var workingRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "module-compilation", Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(workingRoot, "output");
        var candidatePath = stagingPath + ".compiled-" + Guid.NewGuid().ToString("N");
        var backupPath = stagingPath + ".script-backup-" + Guid.NewGuid().ToString("N");
        var stagingInputSha256 = ComputeInventorySha256(BuildFileInventory(stagingPath));

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
            var candidateCompilationManifest = Path.Combine(candidatePath, moduleName + ".powerforge-compilation.json");
            if (!File.Exists(candidateManifest) || !File.Exists(candidateAssembly))
                throw new InvalidOperationException("The generated binary module did not contain its expected manifest and assembly.");
            if (string.IsNullOrWhiteSpace(compiled.ManifestPath) || !File.Exists(compiled.ManifestPath))
                throw new InvalidOperationException("The generated binary module did not contain canonical compilation evidence.");
            File.Copy(compiled.ManifestPath, candidateCompilationManifest, overwrite: true);
            var exports = ModuleManifestExportReader.ReadExports(candidateManifest);

            ReplaceDirectory(stagingPath, candidatePath, backupPath);
            var stagedManifest = Path.Combine(stagingPath, moduleName + ".psd1");
            var stagedAssembly = Path.Combine(stagingPath, moduleName + ".dll");
            var stagedCompilationManifest = Path.Combine(stagingPath, moduleName + ".powerforge-compilation.json");

            var manifest = compiled.Manifest;
            var finalizedPayloadFiles = EnumeratePayloadFiles(stagingPath);
            var result = CreateResult(
                manifest,
                stagedAssembly,
                stagedManifest,
                stagedCompilationManifest,
                finalizedPayloadFiles,
                stagingInputSha256);
            releaseContract.ValidateStagedManifest(
                stagedManifest,
                moduleName + (manifest.UsesPowerShellRuntimeFallback ? ".psm1" : ".dll"));
            return (
                new ModuleBuildResult(stagingPath, stagedManifest, exports, buildResult.BuildNotes, finalizedPayloadFiles),
                result);
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
        PowerShellModuleCompilationConfiguration configuration,
        PowerShellModuleCompilationReleaseContract releaseContract,
        SigningOptionsConfiguration? signing)
    {
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (releaseContract is null) throw new ArgumentNullException(nameof(releaseContract));
        ValidateDependencyLock(configuration.DependencyLock);

        var stagingPath = Path.GetFullPath(buildResult.StagingPath);
        var moduleName = Path.GetFileNameWithoutExtension(buildResult.ManifestPath);
        var checkpointPath = GetCheckpointPath(stagingPath, moduleName);
        var receiptPath = GetCheckpointReceiptPath(stagingPath, moduleName);
        if (!File.Exists(checkpointPath))
            throw new InvalidOperationException($"Reusable compiled staging is missing its compilation checkpoint: '{checkpointPath}'.");
        ValidateCheckpointAuthority(checkpointPath, receiptPath, signing);

        var checkpoint = JsonSerializer.Deserialize<CompilationCheckpoint>(
                             File.ReadAllText(checkpointPath),
                             CreateCheckpointJsonOptions())
                         ?? throw new InvalidOperationException("Reusable compiled staging contains an unreadable compilation checkpoint.");
        var contractMismatches = new List<string>();
        if (checkpoint.SchemaVersion != CheckpointSchemaVersion) contractMismatches.Add("schema");
        if (!string.Equals(checkpoint.ArtifactName, moduleName, StringComparison.Ordinal)) contractMismatches.Add("artifact name");
        if (checkpoint.Mode != configuration.Mode) contractMismatches.Add("mode");
        if (!string.Equals(checkpoint.TargetFramework, configuration.TargetFramework, StringComparison.Ordinal)) contractMismatches.Add("target framework");
        if (!string.Equals(checkpoint.AssemblyFileName, moduleName + ".dll", StringComparison.Ordinal)) contractMismatches.Add("assembly");
        if (!string.Equals(checkpoint.ReleaseContractSha256, releaseContract.Sha256, StringComparison.OrdinalIgnoreCase)) contractMismatches.Add("release plan");
        if (string.IsNullOrWhiteSpace(checkpoint.StagingInputSha256)) contractMismatches.Add("staging input");
        if (string.IsNullOrWhiteSpace(checkpoint.SigningCertificateThumbprint)) contractMismatches.Add("signing identity");
        if (checkpoint.ResourceMode != configuration.ResourceMode) contractMismatches.Add("resource mode");
        if (!(checkpoint.IncludeResource ?? Array.Empty<string>()).SequenceEqual(NormalizePatterns(configuration.IncludeResource), StringComparer.Ordinal)) contractMismatches.Add("included resources");
        if (!(checkpoint.ExcludeResource ?? Array.Empty<string>()).SequenceEqual(NormalizePatterns(configuration.ExcludeResource), StringComparer.Ordinal)) contractMismatches.Add("excluded resources");
        if (!string.Equals(checkpoint.DependencyLockSha256, configuration.DependencyLock?.LockSha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase)) contractMismatches.Add("dependency lock");
        if (checkpoint.AllowUnreviewedDependencies != configuration.AllowUnreviewedDependencies) contractMismatches.Add("dependency review policy");
        if (contractMismatches.Count > 0)
            throw new InvalidOperationException(
                $"Reusable compiled staging does not match the requested PowerShell compilation contract: {string.Join(", ", contractMismatches)}.");
        var configuredSigner = PowerShellCompilationEvidenceAuthenticator.GetSignerThumbprint(
            signing ?? throw new InvalidOperationException(
                "Reusable compiled staging requires configured signing options for authenticated checkpoint verification."));
        if (!string.Equals(
                checkpoint.SigningCertificateThumbprint,
                configuredSigner,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Reusable compiled staging checkpoint was not authorized by the currently configured signing identity.");
        }

        var assemblyPath = Path.Combine(stagingPath, checkpoint.AssemblyFileName);
        var compilationManifestPath = Path.Combine(stagingPath, moduleName + ".powerforge-compilation.json");
        var actualFiles = BuildFileInventory(stagingPath, checkpointPath, receiptPath);
        if (!InventoryMatches(checkpoint.Files ?? Array.Empty<CheckpointFile>(), actualFiles))
            throw new InvalidOperationException("Reusable compiled staging payload does not match its compilation checkpoint.");

        if (!File.Exists(assemblyPath) || !File.Exists(compilationManifestPath))
            throw new InvalidOperationException("Reusable compiled staging is missing its assembly or canonical compilation evidence.");
        var manifest = JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(
                           File.ReadAllText(compilationManifestPath),
                           CreateCompilationManifestJsonOptions())
                       ?? throw new InvalidOperationException("Reusable compiled staging contains unreadable canonical compilation evidence.");
        ValidateCanonicalManifest(manifest, moduleName, configuration, assemblyPath);
        releaseContract.ValidateStagedManifest(
            buildResult.ManifestPath,
            moduleName + (manifest.UsesPowerShellRuntimeFallback ? ".psm1" : ".dll"));
        var finalizedPayloadFiles = EnumeratePayloadFiles(stagingPath, checkpointPath, receiptPath);
        var result = CreateResult(
            manifest,
            assemblyPath,
            buildResult.ManifestPath,
            compilationManifestPath,
            finalizedPayloadFiles,
            checkpoint.StagingInputSha256);
        var exports = ModuleManifestExportReader.ReadExports(buildResult.ManifestPath);
        return (
            new ModuleBuildResult(stagingPath, buildResult.ManifestPath, exports, buildResult.BuildNotes, finalizedPayloadFiles),
            result);
    }

    public void PersistCheckpoint(
        PowerShellModuleCompilationResult result,
        PowerShellModuleCompilationConfiguration configuration,
        PowerShellModuleCompilationReleaseContract releaseContract,
        ModuleSigningResult? signingResult,
        SigningOptionsConfiguration? signing)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (releaseContract is null) throw new ArgumentNullException(nameof(releaseContract));
        ValidateDependencyLock(configuration.DependencyLock);
        if (!File.Exists(result.AssemblyPath))
            throw new FileNotFoundException("Compiled module assembly was not found while writing its checkpoint.", result.AssemblyPath);

        var stagingPath = Path.GetDirectoryName(Path.GetFullPath(result.ModuleManifestPath))
                          ?? throw new InvalidOperationException("Compiled module manifest has no containing directory.");
        var moduleName = Path.GetFileNameWithoutExtension(result.ModuleManifestPath);
        var checkpointPath = GetCheckpointPath(stagingPath, moduleName);
        var receiptPath = GetCheckpointReceiptPath(stagingPath, moduleName);
        var canonicalEvidenceSignaturePath = GetCanonicalEvidenceSignaturePath(stagingPath, moduleName);
        var manifest = FinalizeCanonicalManifest(
            result.CompilationManifestPath,
            stagingPath,
            result.AssemblyPath,
            result.ModuleManifestPath,
            checkpointPath,
            receiptPath,
            signingResult,
            canonicalEvidenceSignaturePath: canonicalEvidenceSignaturePath);
        if (signing is null)
        {
            if (File.Exists(canonicalEvidenceSignaturePath)) File.Delete(canonicalEvidenceSignaturePath);
        }
        else
        {
            var canonicalAuthority = PowerShellCompilationEvidenceAuthenticator.Sign(
                File.ReadAllBytes(result.CompilationManifestPath),
                signing);
            WriteBytesAtomically(canonicalEvidenceSignaturePath, canonicalAuthority.Signature);
        }
        var checkpoint = new CompilationCheckpoint
        {
            SchemaVersion = CheckpointSchemaVersion,
            ArtifactName = moduleName,
            Mode = result.Mode,
            TargetFramework = result.TargetFramework,
            ReleaseContractSha256 = releaseContract.Sha256,
            AssemblyFileName = Path.GetFileName(result.AssemblyPath),
            ResourceMode = configuration.ResourceMode,
            IncludeResource = NormalizePatterns(configuration.IncludeResource),
            ExcludeResource = NormalizePatterns(configuration.ExcludeResource),
            DependencyLockSha256 = configuration.DependencyLock?.LockSha256 ?? string.Empty,
            AllowUnreviewedDependencies = configuration.AllowUnreviewedDependencies,
            StagingInputSha256 = result.StagingInputSha256,
            SigningCertificateThumbprint = signing is null
                ? string.Empty
                : PowerShellCompilationEvidenceAuthenticator.GetSignerThumbprint(signing),
            Files = BuildFileInventory(stagingPath, checkpointPath, receiptPath)
        };
        var checkpointBytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, CreateCheckpointJsonOptions());
        WriteBytesAtomically(checkpointPath, checkpointBytes);
        if (signing is null)
        {
            if (File.Exists(receiptPath)) File.Delete(receiptPath);
        }
        else
        {
            var authority = PowerShellCompilationEvidenceAuthenticator.Sign(checkpointBytes, signing);
            if (!string.Equals(
                    authority.Thumbprint,
                    checkpoint.SigningCertificateThumbprint,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Compilation checkpoint signing identity changed while evidence was being finalized.");
            WriteBytesAtomically(receiptPath, authority.Signature);
        }

        var finalizedPayloadFiles = EnumeratePayloadFiles(stagingPath, checkpointPath, receiptPath);
        ApplyManifestSummary(result, manifest);
        result.FinalizedPayloadFiles = finalizedPayloadFiles;
    }

    internal static string? FinalizeDeliveredCanonicalManifest(
        string moduleRoot,
        string moduleName,
        ModuleSigningResult? signingResult,
        SigningOptionsConfiguration? signing)
    {
        var root = Path.GetFullPath(moduleRoot);
        var compilationManifestPath = Path.Combine(root, moduleName + ".powerforge-compilation.json");
        if (!File.Exists(compilationManifestPath)) return null;
        var evidenceSignaturePath = Path.Combine(root, moduleName + ".powerforge-compilation.p7s");
        _ = FinalizeCanonicalManifest(
            compilationManifestPath,
            root,
            Path.Combine(root, moduleName + ".dll"),
            Path.Combine(root, moduleName + ".psd1"),
            evidenceSignaturePath,
            evidenceSignaturePath,
            signingResult,
            portablePaths: true,
            canonicalEvidenceSignaturePath: evidenceSignaturePath);

        if (signing is null)
        {
            if (File.Exists(evidenceSignaturePath)) File.Delete(evidenceSignaturePath);
            return null;
        }

        var authority = PowerShellCompilationEvidenceAuthenticator.Sign(
            File.ReadAllBytes(compilationManifestPath),
            signing);
        WriteBytesAtomically(evidenceSignaturePath, authority.Signature);
        return evidenceSignaturePath;
    }

    private static PowerShellModuleCompilationResult CreateResult(
        PowerShellCompilationArtifactManifest manifest,
        string assemblyPath,
        string moduleManifestPath,
        string compilationManifestPath,
        IReadOnlyList<string> finalizedPayloadFiles,
        string stagingInputSha256)
    {
        var result = new PowerShellModuleCompilationResult
        {
            AssemblyPath = assemblyPath,
            ModuleManifestPath = moduleManifestPath,
            CompilationManifestPath = compilationManifestPath,
            FinalizedPayloadFiles = finalizedPayloadFiles,
            StagingInputSha256 = stagingInputSha256
        };
        ApplyManifestSummary(result, manifest);
        return result;
    }

    private static void ApplyManifestSummary(
        PowerShellModuleCompilationResult result,
        PowerShellCompilationArtifactManifest manifest)
    {
        result.Mode = manifest.Mode;
        result.TargetFramework = manifest.TargetFramework;
        result.AnalyzedUnits = manifest.AnalyzedUnits;
        result.EmittedUnits = manifest.EmittedUnits;
        result.RuntimeRoutedUnits = manifest.RuntimeRoutedUnits;
        result.FallbackUnits = manifest.FallbackUnits;
        result.ShapedFallbackUnits = manifest.ShapedFallbackUnits;
        result.CoveragePercentage = manifest.CompilationCoveragePercentage;
        result.UsesPowerShellRuntimeFallback = manifest.UsesPowerShellRuntimeFallback;
    }

    private static void ValidateCanonicalManifest(
        PowerShellCompilationArtifactManifest manifest,
        string moduleName,
        PowerShellModuleCompilationConfiguration configuration,
        string assemblyPath)
    {
        if (manifest.SchemaVersion < 7 ||
            manifest.Kind != PowerShellCompilationArtifactKind.BinaryModule ||
            !string.Equals(manifest.ArtifactName, moduleName, StringComparison.Ordinal) ||
            manifest.Mode != configuration.Mode ||
            !string.Equals(manifest.TargetFramework, configuration.TargetFramework, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Reusable compiled staging canonical evidence does not match the requested compilation contract.");
        }

        if (manifest.DependencyGraph is not null)
            PowerShellCompilationDependencyLockHasher.EnsureValid(manifest.DependencyGraph, "canonical compilation evidence");
        PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);
        if (configuration.DependencyLock is not null &&
            !string.Equals(
                manifest.DependencyGraph?.LockSha256,
                configuration.DependencyLock.LockSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Reusable compiled staging dependency evidence does not match the reviewed dependency lock.");
        }

        var assemblyEvidence = (manifest.Files ?? Array.Empty<PowerShellCompilationArtifactFile>())
            .SingleOrDefault(file => PowerShellCompilationPathSafety.PathEquals(file.Path, assemblyPath));
        if (assemblyEvidence is null ||
            !File.Exists(assemblyPath) ||
            !string.Equals(assemblyEvidence.Sha256, ComputeSha256(assemblyPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Reusable compiled staging assembly does not match its canonical compilation evidence.");
        }
    }

    private static PowerShellCompilationArtifactManifest FinalizeCanonicalManifest(
        string compilationManifestPath,
        string stagingPath,
        string assemblyPath,
        string moduleManifestPath,
        string checkpointPath,
        string receiptPath,
        ModuleSigningResult? signingResult,
        bool portablePaths = false,
        string? canonicalEvidenceSignaturePath = null)
    {
        if (!File.Exists(compilationManifestPath))
            throw new InvalidOperationException("Canonical PowerShell compilation evidence is missing from module staging.");

        var manifest = JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(
                           File.ReadAllText(compilationManifestPath),
                           CreateCompilationManifestJsonOptions())
                       ?? throw new InvalidOperationException("Canonical PowerShell compilation evidence is unreadable.");
        var previousRoot = string.IsNullOrWhiteSpace(manifest.ArtifactPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(manifest.ArtifactPath));
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(previousRoot))
        {
            foreach (var file in manifest.Files ?? Array.Empty<PowerShellCompilationArtifactFile>())
            {
                if (string.IsNullOrWhiteSpace(file.Path)) continue;
                var relative = FrameworkCompatibility.GetRelativePath(previousRoot!, Path.GetFullPath(file.Path)).Replace('\\', '/');
                roles[relative] = file.Role;
            }
        }

        var payloadFiles = EnumeratePayloadFiles(
            stagingPath,
            compilationManifestPath,
            checkpointPath,
            receiptPath,
            canonicalEvidenceSignaturePath ?? string.Empty);
        manifest.SchemaVersion = Math.Max(manifest.SchemaVersion, 8);
        manifest.Files = payloadFiles.Select(path =>
        {
            var relative = FrameworkCompatibility.GetRelativePath(stagingPath, path).Replace('\\', '/');
            return new PowerShellCompilationArtifactFile
            {
                Path = portablePaths ? relative : path,
                RelativePath = relative,
                Role = roles.TryGetValue(relative, out var role) ? role : "FinalizedPayload",
                Sha256 = ComputeSha256(path),
                SizeBytes = new FileInfo(path).Length
            };
        }).ToArray();
        manifest.ArtifactPath = portablePaths
            ? FrameworkCompatibility.GetRelativePath(stagingPath, moduleManifestPath).Replace('\\', '/')
            : moduleManifestPath;
        manifest.ArtifactRelativePath = FrameworkCompatibility.GetRelativePath(stagingPath, moduleManifestPath).Replace('\\', '/');
        manifest.ArtifactSha256 = ComputeSha256(moduleManifestPath);
        manifest.ArtifactSizeBytes = new FileInfo(moduleManifestPath).Length;
        manifest.AuthenticodeSigned = signingResult is not null && signingResult.Success && signingResult.VerifiedFilePaths.Length > 0;
        manifest.AuthenticodeSignedFiles = signingResult?.VerifiedFilePaths.Length ?? 0;
        manifest.SigningCertificateThumbprint = signingResult?.CertificateThumbprint;
        PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);
        WriteTextAtomically(
            compilationManifestPath,
            JsonSerializer.Serialize(manifest, CreateCompilationManifestJsonOptions()));
        return manifest;
    }

    private static JsonSerializerOptions CreateCompilationManifestJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static JsonSerializerOptions CreateCheckpointJsonOptions()
        => new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

    private static void ValidateDependencyLock(PowerShellCompilationDependencyGraph? dependencyLock)
    {
        if (dependencyLock is not null)
            PowerShellCompilationDependencyLockHasher.EnsureValid(dependencyLock, "PowerShell module compilation dependency lock");
    }

    private static void ValidateCheckpointAuthority(
        string checkpointPath,
        string receiptPath,
        SigningOptionsConfiguration? signing)
    {
        if (!File.Exists(receiptPath))
            throw new InvalidOperationException($"Reusable compiled staging is missing its authenticated checkpoint signature: '{receiptPath}'.");
        if (signing is null)
            throw new InvalidOperationException(
                "Reusable compiled staging requires configured signing options for authenticated checkpoint verification.");
        PowerShellCompilationEvidenceAuthenticator.Verify(
            File.ReadAllBytes(checkpointPath),
            File.ReadAllBytes(receiptPath),
            signing);
    }

    private static void WriteTextAtomically(string path, string content)
        => WriteBytesAtomically(path, System.Text.Encoding.UTF8.GetBytes(content));

    private static void WriteBytesAtomically(string path, byte[] content)
    {
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tempPath, content);
            if (File.Exists(path))
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
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

    private static string GetCheckpointReceiptPath(string stagingPath, string moduleName)
        => Path.Combine(stagingPath, moduleName + ".powerforge-module-compilation.p7s");

    private static string GetCanonicalEvidenceSignaturePath(string stagingPath, string moduleName)
        => Path.Combine(stagingPath, moduleName + ".powerforge-compilation.p7s");

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

    private static string[] EnumeratePayloadFiles(string stagingPath, params string[] excludedPaths)
    {
        EnsureNotReparsePoint(stagingPath);
        var excluded = new HashSet<string>(
            (excludedPaths ?? Array.Empty<string>())
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath),
            PowerShellCompilationPathSafety.PathComparer);
        var files = new List<string>();
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
                var fullPath = Path.GetFullPath(entry);
                if (!excluded.Contains(fullPath)) files.Add(fullPath);
            }
        }
        return files
            .OrderBy(path => FrameworkCompatibility.GetRelativePath(stagingPath, path), StringComparer.Ordinal)
            .ToArray();
    }

    private static CheckpointFile[] BuildFileInventory(string stagingPath, params string[] excludedPaths)
        => EnumeratePayloadFiles(stagingPath, excludedPaths)
            .Select(path => new CheckpointFile
            {
                Path = FrameworkCompatibility.GetRelativePath(stagingPath, path).Replace('\\', '/'),
                SizeBytes = new FileInfo(path).Length,
                Sha256 = ComputeSha256(path)
            })
            .ToArray();

    private static string ComputeInventorySha256(CheckpointFile[] files)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(files.Select(static file => new
        {
            file.Path,
            file.SizeBytes,
            file.Sha256
        }).ToArray());
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes)
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
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
        public string ReleaseContractSha256 { get; set; } = string.Empty;
        public string AssemblyFileName { get; set; } = string.Empty;
        public PowerShellCompilationResourceMode ResourceMode { get; set; }
        public string[] IncludeResource { get; set; } = Array.Empty<string>();
        public string[] ExcludeResource { get; set; } = Array.Empty<string>();
        public string DependencyLockSha256 { get; set; } = string.Empty;
        public bool AllowUnreviewedDependencies { get; set; }
        public string StagingInputSha256 { get; set; } = string.Empty;
        public string SigningCertificateThumbprint { get; set; } = string.Empty;
        public CheckpointFile[] Files { get; set; } = Array.Empty<CheckpointFile>();
    }

    private sealed class CheckpointFile
    {
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}
