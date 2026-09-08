namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    internal static IReadOnlyDictionary<string, ModuleArtifactSnapshot> CaptureModuleArtifactBaseline(
        IEnumerable<string>? configuredPaths,
        PowerForgeModuleReleasePlanSummary? plan = null)
        => EnumerateModuleArtifactFiles(configuredPaths, plan)
            .ToDictionary(
                static path => path,
                CaptureModuleArtifactSnapshot,
                StringComparer.OrdinalIgnoreCase);

    internal static string[] ResolveProducedModuleArtifacts(
        IEnumerable<string>? configuredPaths,
        IReadOnlyDictionary<string, ModuleArtifactSnapshot>? baseline,
        PowerForgeModuleReleasePlanSummary? plan = null)
    {
        var prior = baseline ?? new Dictionary<string, ModuleArtifactSnapshot>(StringComparer.OrdinalIgnoreCase);
        return EnumerateModuleArtifactFiles(configuredPaths, plan)
            .Select(static path => (Path: path, Snapshot: CaptureModuleArtifactSnapshot(path)))
            .Where(item => !prior.TryGetValue(item.Path, out var previous) || !previous.Equals(item.Snapshot))
            .Select(static item => item.Path)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateModuleArtifactFiles(
        IEnumerable<string>? configuredPaths,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            if (output is null || output.Type != ArtefactType.Script)
                continue;

            string outputRoot = string.IsNullOrWhiteSpace(output.OutputPath)
                ? output.OutputRoot
                : output.OutputPath!;
            if (!ArtefactLayoutPathResolver.TryResolveScriptOutputEntryPointPath(
                    outputRoot,
                    output.EntryPointRelativePath,
                    out string? entryPointPath) ||
                !File.Exists(entryPointPath))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(entryPointPath!);
            if (seen.Add(fullPath))
                yield return fullPath;
        }
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
