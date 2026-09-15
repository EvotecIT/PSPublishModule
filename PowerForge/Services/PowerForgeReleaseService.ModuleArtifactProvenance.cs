namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static readonly StringComparer ModuleArtifactPathComparer = FrameworkCompatibility.PathComparer;
    private static readonly StringComparer ModuleReleaseAssetPathComparer = StringComparer.OrdinalIgnoreCase;

    internal static IReadOnlyDictionary<string, ModuleArtifactSnapshot> CaptureModuleArtifactBaseline(
        IEnumerable<string>? configuredPaths,
        PowerForgeModuleReleasePlanSummary? plan = null)
        => EnumerateModuleArtifactFiles(configuredPaths, plan)
            .ToDictionary(
                static path => path,
                CaptureModuleArtifactSnapshot,
                ModuleArtifactPathComparer);

    internal static string[] ResolveProducedModuleArtifacts(
        IEnumerable<string>? configuredPaths,
        IReadOnlyDictionary<string, ModuleArtifactSnapshot>? baseline,
        PowerForgeModuleReleasePlanSummary? plan = null,
        string? scriptArchiveRoot = null)
    {
        ValidateReportedScriptLayouts(plan);
        var prior = baseline ?? new Dictionary<string, ModuleArtifactSnapshot>(ModuleArtifactPathComparer);
        string[] produced = EnumerateModuleArtifactFiles(configuredPaths, plan)
            .Select(static path => (Path: path, Snapshot: CaptureModuleArtifactSnapshot(path)))
            .Where(item => !prior.TryGetValue(item.Path, out var previous) || !previous.Equals(item.Snapshot))
            .Select(static item => item.Path)
            .OrderBy(static path => path, ModuleArtifactPathComparer)
            .ToArray();
        return ReplaceProducedScriptLayoutsWithArchives(produced, plan, scriptArchiveRoot);
    }

    private static IEnumerable<string> EnumerateModuleArtifactFiles(
        IEnumerable<string>? configuredPaths,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        var seen = new HashSet<string>(ModuleArtifactPathComparer);
        foreach (var configuredPath in (configuredPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            foreach (var candidate in PathTokenCandidateResolver.ResolveExistingPaths(configuredPath))
            {
                if (File.Exists(candidate))
                {
                    var fullPath = Path.GetFullPath(candidate);
                    if (seen.Add(fullPath))
                        yield return fullPath;
                    continue;
                }

                if (!Directory.Exists(candidate))
                    continue;

                foreach (var file in Directory.EnumerateFiles(candidate, "*", SearchOption.TopDirectoryOnly))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (seen.Add(fullPath))
                        yield return fullPath;
                }
            }
        }

        foreach (PowerForgeModuleArtefactOutputSummary output in
                 plan?.ArtefactOutputs ?? Array.Empty<PowerForgeModuleArtefactOutputSummary>())
        {
            if (output is null ||
                output.Type != ArtefactType.Script ||
                !TryResolveScriptLayout(output, out string? outputRoot, out _))
                continue;

            foreach (string file in Directory.EnumerateFiles(outputRoot!, "*", SearchOption.AllDirectories))
            {
                string fullPath = Path.GetFullPath(file);
                if (seen.Add(fullPath))
                    yield return fullPath;
            }
        }
    }

    private static string[] ReplaceProducedScriptLayoutsWithArchives(
        IReadOnlyCollection<string> producedPaths,
        PowerForgeModuleReleasePlanSummary? plan,
        string? scriptArchiveRoot)
    {
        PowerForgeModuleArtefactOutputSummary[] outputs =
            plan?.ArtefactOutputs ?? Array.Empty<PowerForgeModuleArtefactOutputSummary>();
        var scriptOutputs = outputs
            .Where(static output => output is not null && output.Type == ArtefactType.Script)
            .ToArray();
        if (scriptOutputs.Length == 0 || producedPaths.Count == 0)
            return producedPaths.OrderBy(static path => path, ModuleArtifactPathComparer).ToArray();

        var producedSet = new HashSet<string>(producedPaths, ModuleArtifactPathComparer);
        var portableProducedSet = new HashSet<string>(producedPaths, ModuleReleaseAssetPathComparer);
        var candidates = new List<(
            PowerForgeModuleArtefactOutputSummary Output,
            string OutputRoot,
            string EntryPointPath,
            string[] LayoutFiles,
            string ArchivePath)>();
        foreach (PowerForgeModuleArtefactOutputSummary output in scriptOutputs)
        {
            if (!TryResolveScriptLayout(output, out string? outputRoot, out string? entryPointPath))
                continue;

            string[] layoutFiles = Directory
                .EnumerateFiles(outputRoot!, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToArray();
            if (!layoutFiles.Any(producedSet.Contains))
                continue;

            if (string.IsNullOrWhiteSpace(scriptArchiveRoot))
            {
                throw new InvalidOperationException(
                    $"Script artefact layout '{outputRoot}' was produced, but no task-scoped release archive directory was provided.");
            }

            string fullArchiveRoot = Path.GetFullPath(scriptArchiveRoot!);
            if (ReleasePathsOverlap(fullArchiveRoot, outputRoot!))
            {
                throw new InvalidOperationException(
                    $"Script release archive directory '{fullArchiveRoot}' overlaps its source layout '{outputRoot}'.");
            }

            string archiveName = Path.GetFileNameWithoutExtension(entryPointPath) + ".zip";
            string archivePath = Path.GetFullPath(Path.Combine(fullArchiveRoot, archiveName));
            candidates.Add((output, outputRoot!, entryPointPath!, layoutFiles, archivePath));
        }

        var uniqueCandidates = new List<(
            PowerForgeModuleArtefactOutputSummary Output,
            string OutputRoot,
            string EntryPointPath,
            string[] LayoutFiles,
            string ArchivePath)>();
        var archiveSources = new Dictionary<string, (string ArchivePath, string Source)>(ModuleReleaseAssetPathComparer);
        foreach (var candidate in candidates)
        {
            bool collidesWithProducedAsset = portableProducedSet.Contains(candidate.ArchivePath);
            bool collidesWithRecordedOutput = outputs.Any(other =>
                other is not null &&
                !ReferenceEquals(other, candidate.Output) &&
                ModuleArtefactOutputUsesPath(other, candidate.ArchivePath, ModuleReleaseAssetPathComparer));
            if (collidesWithProducedAsset || collidesWithRecordedOutput)
            {
                throw new InvalidOperationException(
                    $"Script release archive '{candidate.ArchivePath}' collides with another produced or recorded artefact output " +
                    "in the case-insensitive release asset namespace. Configure a unique Script entry-point name or release archive directory.");
            }

            if (archiveSources.TryGetValue(candidate.ArchivePath, out var existing))
            {
                if (ModuleArtifactPathComparer.Equals(existing.ArchivePath, candidate.ArchivePath) &&
                    ModuleArtifactPathComparer.Equals(existing.Source, candidate.OutputRoot))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Script release layouts '{existing.Source}' and '{candidate.OutputRoot}' resolve to archives " +
                    $"'{existing.ArchivePath}' and '{candidate.ArchivePath}' that collide in the case-insensitive release asset namespace. " +
                    "Configure unique script entry-point names.");
            }

            archiveSources.Add(candidate.ArchivePath, (candidate.ArchivePath, candidate.OutputRoot));
            uniqueCandidates.Add(candidate);
        }

        var consumed = new HashSet<string>(ModuleArtifactPathComparer);
        var archives = new List<string>();
        foreach (var candidate in uniqueCandidates)
        {
            ArtefactBuilder.CreateDeterministicZipFromDirectoryContents(
                candidate.OutputRoot,
                candidate.ArchivePath,
                ArtefactBuilder.ScriptStartsWithShebang(candidate.EntryPointPath)
                    ? new[] { candidate.Output.EntryPointRelativePath! }
                    : Array.Empty<string>());
            if (!PowerShellScriptArchiveValidator.TryValidate(
                    candidate.ArchivePath,
                    candidate.Output.EntryPointRelativePath,
                    out string? validationError))
            {
                throw new InvalidOperationException(
                    $"Script release archive '{candidate.ArchivePath}' is not a valid portable release payload: {validationError}");
            }
            candidate.Output.ReleaseAssetPath = candidate.ArchivePath;
            archives.Add(candidate.ArchivePath);

            foreach (string layoutFile in candidate.LayoutFiles.Where(producedSet.Contains))
            {
                bool producedByAnotherArtefact = outputs.Any(other =>
                    other is not null &&
                    other.Type != ArtefactType.Script &&
                    !string.IsNullOrWhiteSpace(other.OutputPath) &&
                    ModuleArtifactPathComparer.Equals(
                        Path.GetFullPath(other.OutputPath!),
                        layoutFile));
                if (!producedByAnotherArtefact)
                    consumed.Add(layoutFile);
            }
        }

        return producedPaths
            .Where(path => !consumed.Contains(path))
            .Concat(archives)
            .Distinct(ModuleArtifactPathComparer)
            .OrderBy(static path => path, ModuleArtifactPathComparer)
            .ToArray();
    }

    private static bool ModuleArtefactOutputUsesPath(
        PowerForgeModuleArtefactOutputSummary output,
        string path,
        StringComparer comparer)
        => (!string.IsNullOrWhiteSpace(output.OutputPath) &&
            comparer.Equals(Path.GetFullPath(output.OutputPath), path)) ||
           (!string.IsNullOrWhiteSpace(output.ReleaseAssetPath) &&
            comparer.Equals(Path.GetFullPath(output.ReleaseAssetPath), path));

    private static void ValidateReportedScriptLayouts(PowerForgeModuleReleasePlanSummary? plan)
    {
        foreach (PowerForgeModuleArtefactOutputSummary output in
                 plan?.ArtefactOutputs ?? Array.Empty<PowerForgeModuleArtefactOutputSummary>())
        {
            if (output is null ||
                output.Type != ArtefactType.Script ||
                TryResolveScriptLayout(output, out _, out _))
            {
                continue;
            }

            string outputRoot = string.IsNullOrWhiteSpace(output.OutputPath)
                ? output.OutputRoot ?? "(missing output path)"
                : output.OutputPath!;
            string entryPoint = string.IsNullOrWhiteSpace(output.EntryPointRelativePath)
                ? "(missing entry point)"
                : output.EntryPointRelativePath!;
            throw new InvalidOperationException(
                $"Reported Script artefact output '{outputRoot}' does not contain its entry point '{entryPoint}'. " +
                "A successful module build must leave the complete Script layout available for release packaging.");
        }
    }

    private static bool TryResolveScriptLayout(
        PowerForgeModuleArtefactOutputSummary output,
        out string? outputRoot,
        out string? entryPointPath)
    {
        outputRoot = string.IsNullOrWhiteSpace(output.OutputPath)
            ? output.OutputRoot
            : output.OutputPath!;
        entryPointPath = null;
        if (string.IsNullOrWhiteSpace(outputRoot))
            return false;

        outputRoot = Path.GetFullPath(outputRoot);
        return Directory.Exists(outputRoot) &&
               ArtefactLayoutPathResolver.TryResolveScriptOutputEntryPointPath(
                   outputRoot,
                   output.EntryPointRelativePath,
                   out entryPointPath) &&
               File.Exists(entryPointPath);
    }

    private static ModuleArtifactSnapshot CaptureModuleArtifactSnapshot(string path)
    {
        var file = new FileInfo(path);
        return new ModuleArtifactSnapshot(
            file.Length,
            file.CreationTimeUtc,
            file.LastWriteTimeUtc,
            ComputeSha256(path));
    }

    internal readonly struct ModuleArtifactSnapshot
    {
        internal ModuleArtifactSnapshot(
            long length,
            DateTime creationTimeUtc,
            DateTime lastWriteTimeUtc,
            string sha256)
        {
            Length = length;
            CreationTimeUtc = creationTimeUtc;
            LastWriteTimeUtc = lastWriteTimeUtc;
            Sha256 = sha256;
        }

        internal long Length { get; }

        internal DateTime CreationTimeUtc { get; }

        internal DateTime LastWriteTimeUtc { get; }

        internal string Sha256 { get; }
    }
}
