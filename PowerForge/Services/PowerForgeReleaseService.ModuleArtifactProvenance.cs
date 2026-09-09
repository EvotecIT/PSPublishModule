namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static readonly StringComparer ModuleArtifactPathComparer = FrameworkCompatibility.PathComparer;

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

        var consumed = new HashSet<string>(ModuleArtifactPathComparer);
        var producedSet = new HashSet<string>(producedPaths, ModuleArtifactPathComparer);
        var archives = new List<string>();
        var archiveSources = new Dictionary<string, string>(ModuleArtifactPathComparer);
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
            if (archiveSources.TryGetValue(archivePath, out string? existingSource) &&
                !ModuleArtifactPathComparer.Equals(existingSource, outputRoot))
            {
                throw new InvalidOperationException(
                    $"Script release layouts '{existingSource}' and '{outputRoot}' resolve to the same archive '{archivePath}'. " +
                    "Configure unique script entry-point names.");
            }

            archiveSources[archivePath] = outputRoot!;
            ArtefactBuilder.CreateDeterministicZipFromDirectoryContents(
                outputRoot!,
                archivePath,
                ArtefactBuilder.ScriptStartsWithShebang(entryPointPath!)
                    ? new[] { output.EntryPointRelativePath! }
                    : Array.Empty<string>());
            if (!PowerShellScriptArchiveValidator.TryValidate(
                    archivePath,
                    output.EntryPointRelativePath,
                    out string? validationError))
            {
                throw new InvalidOperationException(
                    $"Script release archive '{archivePath}' is not a valid portable release payload: {validationError}");
            }
            output.ReleaseAssetPath = archivePath;
            if (!archives.Contains(archivePath, ModuleArtifactPathComparer))
                archives.Add(archivePath);

            foreach (string layoutFile in layoutFiles.Where(producedSet.Contains))
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
