using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace PowerForge.Web.Cli;

/// <summary>Which part of a split pipeline run executes in this process.</summary>
internal enum WebPipelineSplitPhase
{
    /// <summary>Steps before the apidocs block plus this shard's share of the apidocs work.</summary>
    Prepare,
    /// <summary>Merges every shard archive, then runs the steps after the apidocs block.</summary>
    Finish
}

/// <summary>
/// Options for running one pipeline across several machines. Every shard runs the steps before the
/// contiguous apidocs block and a balanced share of its generator runs, and records the files that share
/// changed. The finishing run merges all shard archives and runs the remaining steps.
/// </summary>
internal sealed class WebPipelineSplitOptions
{
    public WebPipelineSplitPhase Phase { get; init; }
    public int ShardIndex { get; init; } = 1;
    public int ShardCount { get; init; } = 1;
    public string? ArchivePath { get; init; }
    public string? ArchivesDirectory { get; init; }

    /// <summary>Validates CLI values. Returns null when no split option was given.</summary>
    public static WebPipelineSplitOptions? Parse(string? phase, string? shard, string? archive, string? archives)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            if (!string.IsNullOrWhiteSpace(shard) || !string.IsNullOrWhiteSpace(archive) || !string.IsNullOrWhiteSpace(archives))
                throw new ArgumentException("--apidocs-shard, --split-archive and --split-archives require --split-phase.");
            return null;
        }

        switch (phase.Trim().ToLowerInvariant())
        {
            case "prepare":
                if (string.IsNullOrWhiteSpace(archive))
                    throw new ArgumentException("--split-phase prepare requires --split-archive <file>.");
                if (!string.IsNullOrWhiteSpace(archives))
                    throw new ArgumentException("--split-archives is only valid with --split-phase finish.");
                var (index, count) = ParseShard(shard);
                return new WebPipelineSplitOptions
                {
                    Phase = WebPipelineSplitPhase.Prepare,
                    ShardIndex = index,
                    ShardCount = count,
                    ArchivePath = Path.GetFullPath(archive)
                };
            case "finish":
                if (string.IsNullOrWhiteSpace(archives))
                    throw new ArgumentException("--split-phase finish requires --split-archives <directory>.");
                if (!string.IsNullOrWhiteSpace(shard) || !string.IsNullOrWhiteSpace(archive))
                    throw new ArgumentException("--apidocs-shard and --split-archive are only valid with --split-phase prepare.");
                return new WebPipelineSplitOptions
                {
                    Phase = WebPipelineSplitPhase.Finish,
                    ArchivesDirectory = Path.GetFullPath(archives)
                };
            default:
                throw new ArgumentException($"Unsupported --split-phase '{phase}'. Expected prepare or finish.");
        }
    }

    private static (int Index, int Count) ParseShard(string? shard)
    {
        var parts = (shard ?? string.Empty).Split('/');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0].Trim(), out var index) ||
            !int.TryParse(parts[1].Trim(), out var count) ||
            count < 1 || count > 64 || index < 1 || index > count)
        {
            throw new ArgumentException($"--apidocs-shard must be <index>/<count> with 1 <= index <= count <= 64, got '{shard}'.");
        }
        return (index, count);
    }
}

internal static partial class WebPipelineRunner
{
    private const string SplitManifestEntryName = "powerforge-split-manifest.json";
    private const string SplitFileEntryPrefix = "files/";

    // Set while one apidocs batch step runs a shard's subset of its inputs (original input indexes).
    private static readonly AsyncLocal<int[]?> CurrentApiDocsInputSelection = new();

    /// <summary>One generator run of the apidocs block: a whole step or one independent batch input.</summary>
    private sealed class SplitUnit
    {
        public required string Key { get; init; }
        public required int StepIndex { get; init; }
        public int? InputIndex { get; init; }
        public long Weight { get; set; }
        public int Shard { get; set; }
    }

    private sealed class SplitPlan
    {
        public required int FirstIndex { get; init; }
        public required int LastIndex { get; init; }
        public required List<SplitUnit> Units { get; init; }
        public Dictionary<string, (long Length, long Ticks)>? Before { get; set; }
    }

    private sealed class SplitManifest
    {
        public int ShardIndex { get; init; }
        public int ShardCount { get; init; }
        public string[] Units { get; init; } = Array.Empty<string>();
        public Dictionary<string, string> Files { get; init; } = new(StringComparer.Ordinal);
        public string[] Deleted { get; init; } = Array.Empty<string>();
        public string Source { get; init; } = string.Empty;
        public string Engine { get; init; } = string.Empty;
    }

    /// <summary>The contiguous apidocs block that a split run distributes.</summary>
    private static (int First, int Last) ResolveSplitBlock(List<PipelineStepDefinition> steps)
    {
        var apiDocs = steps.Where(static step => step.Task.Equals("apidocs", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (apiDocs.Length == 0)
            throw new InvalidOperationException("Split pipeline runs require at least one apidocs step.");

        var first = apiDocs[0].Index;
        var last = apiDocs[^1].Index;
        var between = steps.FirstOrDefault(step => step.Index > first && step.Index < last &&
                                                    !step.Task.Equals("apidocs", StringComparison.OrdinalIgnoreCase));
        if (between is not null)
            throw new InvalidOperationException($"Split pipeline runs require consecutive apidocs steps; step '{between.Id}' ({between.Task}) is between them.");
        return (first, last);
    }

    /// <summary>
    /// Lists the generator runs of the enabled apidocs steps and assigns them to shards. Independent batch
    /// inputs are separate units. Weights come from the documentation inputs, so every shard that built the
    /// same commit computes the same assignment; the finishing run verifies the coverage either way.
    /// </summary>
    private static SplitPlan BuildSplitPlan(PipelineRunState state, int shardCount)
    {
        var (first, last) = ResolveSplitBlock(state.Steps);
        var units = new List<SplitUnit>();
        var footprints = new List<(SplitUnit Unit, ApiDocsFootprint Footprint)>();
        foreach (var definition in state.Steps.Where(step => step.Index >= first && step.Index <= last))
        {
            if (GetSkipReason(definition.Task, definition.Element, state.EffectiveMode, state.OnlyTaskSet, state.SkipTaskSet) is not null)
                continue;
            if (definition.DependencyIndexes.Any(index => index >= first && index <= last))
                throw new InvalidOperationException($"Split pipeline runs cannot distribute apidocs step '{definition.Id}' because it depends on another apidocs step.");

            var stepFootprints = TryGetApiDocsFootprints(state.BaseDir, definition.Element, definition.Index);
            if (stepFootprints is null)
                throw new InvalidOperationException($"Split pipeline runs cannot distribute apidocs step '{definition.Id}': its output is not declared or it executes PowerShell examples.");
            // Shard archives carry only files under the pipeline root.
            var root = ResolveSplitPhysicalPath(state.BaseDir);
            var outside = stepFootprints.SelectMany(static footprint => footprint.Outputs)
                .FirstOrDefault(output => !IsUnderSplitRoot(ResolveSplitPhysicalPath(output), root));
            if (outside is not null)
                throw new InvalidOperationException($"Split pipeline runs cannot distribute apidocs step '{definition.Id}': output '{outside}' is outside the pipeline root.");

            var inputs = GetArrayOfObjects(definition.Element, "inputs") ?? GetArrayOfObjects(definition.Element, "entries");
            if (inputs is { Length: > 1 } && AreIndependentApiDocsInputs(state.BaseDir, definition.Element))
            {
                for (var index = 0; index < inputs.Length; index++)
                {
                    using var merged = CreateMergedApiDocsStepDocument(definition.Element, inputs[index]);
                    var unit = new SplitUnit
                    {
                        Key = $"{definition.Index}:{definition.Id}#{index}",
                        StepIndex = definition.Index,
                        InputIndex = index,
                        Weight = GetApiDocsUnitWeight(state.BaseDir, merged.RootElement)
                    };
                    units.Add(unit);
                    footprints.Add((unit, stepFootprints[index]));
                }
                continue;
            }

            var whole = new SplitUnit
            {
                Key = $"{definition.Index}:{definition.Id}",
                StepIndex = definition.Index,
                Weight = inputs is { Length: > 0 }
                    ? inputs.Sum(input =>
                    {
                        using var merged = CreateMergedApiDocsStepDocument(definition.Element, input);
                        return GetApiDocsUnitWeight(state.BaseDir, merged.RootElement);
                    })
                    : GetApiDocsUnitWeight(state.BaseDir, definition.Element)
            };
            units.Add(whole);
            footprints.AddRange(stepFootprints.Select(footprint => (whole, footprint)));
        }

        // A unit that reads or shares another unit's files must see it, so it cannot run elsewhere.
        for (var left = 0; left < footprints.Count; left++)
        {
            for (var right = left + 1; right < footprints.Count; right++)
            {
                if (ReferenceEquals(footprints[left].Unit, footprints[right].Unit))
                    continue;
                if (Conflicts(footprints[left].Footprint, footprints[right].Footprint, unitDependsOnExisting: false))
                    throw new InvalidOperationException(
                        $"Split pipeline runs cannot distribute apidocs units '{footprints[left].Unit.Key}' and '{footprints[right].Unit.Key}': they share outputs, routes or read each other's files.");
            }
        }

        AssignSplitShards(units, shardCount);
        return new SplitPlan { FirstIndex = first, LastIndex = last, Units = units };
    }

    /// <summary>Longest-first greedy assignment: the heaviest remaining unit goes to the lightest shard.</summary>
    internal static int[] AssignSplitShards(IReadOnlyList<long> weights, int shardCount)
    {
        if (shardCount < 1)
            throw new ArgumentOutOfRangeException(nameof(shardCount));
        var loads = new long[shardCount];
        var shards = new int[weights.Count];
        foreach (var index in Enumerable.Range(0, weights.Count).OrderByDescending(index => weights[index]).ThenBy(static index => index))
        {
            var target = 0;
            for (var shard = 1; shard < shardCount; shard++)
                if (loads[shard] < loads[target])
                    target = shard;
            shards[index] = target + 1;
            loads[target] += Math.Max(1, weights[index]);
        }
        return shards;
    }

    private static void AssignSplitShards(List<SplitUnit> units, int shardCount)
    {
        var shards = AssignSplitShards(units.Select(static unit => unit.Weight).ToArray(), shardCount);
        for (var index = 0; index < units.Count; index++)
            units[index].Shard = shards[index];
    }

    private static long GetApiDocsUnitWeight(string baseDir, JsonElement step)
    {
        var paths = new List<string?>
        {
            GetString(step, "xml"),
            GetString(step, "assembly"),
            GetString(step, "help") ?? GetString(step, "helpPath") ?? GetString(step, "help-path")
        };
        foreach (var name in new[] { "xmls", "xmlPaths", "assemblies", "assemblyPaths" })
            paths.AddRange(GetArrayOfStrings(step, name) ?? Array.Empty<string>());

        long weight = 0;
        foreach (var path in paths)
        {
            var resolved = ResolvePath(baseDir, path);
            if (string.IsNullOrWhiteSpace(resolved))
                continue;
            if (File.Exists(resolved))
                weight += new FileInfo(resolved).Length;
            else if (Directory.Exists(resolved))
                weight += new DirectoryInfo(resolved).EnumerateFiles("*", SearchOption.AllDirectories).Sum(static file => file.Length);
        }
        return Math.Max(1, weight);
    }

    private static SplitPlan EnsureSplitPlan(PipelineRunState state)
    {
        lock (state.SplitGate)
        {
            if (state.SplitPlan is not null)
                return state.SplitPlan;

            var split = state.Split!;
            var plan = BuildSplitPlan(state, split.Phase == WebPipelineSplitPhase.Prepare ? split.ShardCount : 1);
            if (split.Phase == WebPipelineSplitPhase.Prepare)
            {
                // Taken before any unit of this shard writes, so the archive holds exactly what the shard produced.
                plan.Before = CaptureSplitSnapshot(state.BaseDir, GetSplitExcludedPaths(state));
                var mine = plan.Units.Where(unit => unit.Shard == split.ShardIndex).ToArray();
                state.Logger?.Info($"Split pipeline: shard {split.ShardIndex}/{split.ShardCount} runs {mine.Length} of {plan.Units.Count} apidocs unit(s).");
            }
            state.SplitPlan = plan;
            return plan;
        }
    }

    /// <summary>Reason a step does not run in this split phase, or null when it runs (possibly partially).</summary>
    private static string? GetSplitSkipReason(PipelineRunState state, PipelineStepDefinition definition, out int[]? inputSelection)
    {
        inputSelection = null;
        var split = state.Split;
        if (split is null)
            return null;

        var (first, last) = (state.SplitFirstIndex, state.SplitLastIndex);
        if (split.Phase == WebPipelineSplitPhase.Finish)
            return definition.Index <= last || state.SplitPreparedHere.Contains(definition.Index) ? "split: ran in prepare" : null;

        if (definition.Index > last)
        {
            if (!state.SplitPrepareSteps.Contains(definition.Index))
                return "split: runs in finish";
            if (split.ShardIndex != 1)
                return "split: runs in shard 1";
            // The archive holds only apidocs output, so it is written before these steps change anything.
            EnsureSplitArchiveWritten(state);
            return null;
        }
        if (definition.Index < first)
            return null;

        var plan = EnsureSplitPlan(state);
        var units = plan.Units.Where(unit => unit.StepIndex == definition.Index).ToArray();
        var mine = units.Where(unit => unit.Shard == split.ShardIndex).ToArray();
        if (mine.Length == 0)
            return $"shard {split.ShardIndex}/{split.ShardCount}";
        if (mine.Any(static unit => unit.InputIndex is null) || mine.Length == units.Length)
            return null;
        inputSelection = mine.Select(static unit => unit.InputIndex!.Value).OrderBy(static index => index).ToArray();
        return null;
    }

    /// <summary>
    /// Steps after the apidocs block marked <c>"splitPhase": "prepare"</c>. In a split run they run at the end of
    /// shard 1's prepare run, while the other shards are still generating API docs, instead of in the finish run.
    /// They may depend only on steps before the block or on other such steps.
    /// </summary>
    private static HashSet<int> ResolveSplitPrepareSteps(List<PipelineStepDefinition> steps, int first, int last)
    {
        var selected = new HashSet<int>();
        foreach (var step in steps)
        {
            var phase = GetString(step.Element, "splitPhase");
            if (string.IsNullOrWhiteSpace(phase))
                continue;
            if (!phase.Equals("prepare", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Step '{step.Id}' has unsupported splitPhase '{phase}'; the only supported value is 'prepare'.");
            if (step.Index <= last)
                throw new InvalidOperationException($"Step '{step.Id}' sets splitPhase 'prepare', but only steps after the apidocs block can move to the prepare run.");
            var blocking = step.DependencyIndexes.FirstOrDefault(index => index >= first && !selected.Contains(index));
            if (blocking != 0)
                throw new InvalidOperationException($"Step '{step.Id}' sets splitPhase 'prepare', but depends on step '{steps[blocking - 1].Id}'; it may depend only on steps before the apidocs block or on other splitPhase prepare steps.");
            selected.Add(step.Index);
        }
        return selected;
    }

    private static void EnsureSplitArchiveWritten(PipelineRunState state)
    {
        lock (state.SplitGate)
        {
            if (state.SplitArchiveWritten)
                return;
            WriteSplitArchive(state);
            state.SplitArchiveWritten = true;
        }
    }

    private static string GetSplitPrepareReceiptPath(PipelineRunState state)
        => Path.Combine(Path.GetFullPath(state.BaseDir), ".powerforge", "split-prepare-receipt.json");

    private static string DescribeSplitPrepareStep(PipelineStepDefinition step)
        => $"{step.Index}:{step.Id}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(step.Element.GetRawText()))).ToLowerInvariant()}";

    /// <summary>Removes a receipt left by an earlier prepare run, before this one can change the workspace.</summary>
    private static void DeleteSplitPrepareReceipt(PipelineRunState state)
    {
        var path = GetSplitPrepareReceiptPath(state);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// Records in this workspace that shard 1 ran the splitPhase prepare steps, so a finish run here can skip them.
    /// A finish run elsewhere finds no receipt and runs them itself.
    /// </summary>
    private static void WriteSplitPrepareReceipt(PipelineRunState state)
    {
        var path = GetSplitPrepareReceiptPath(state);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteString("source", ResolveSplitSourceIdentity(state.BaseDir));
        writer.WriteString("engine", ResolveSplitEngineIdentity());
        writer.WriteStartArray("steps");
        // Only steps this run executed; a step skipped by mode or task filters is left to finish.
        foreach (var step in state.Steps.Where(step => state.SplitPrepareSteps.Contains(step.Index) &&
                                                      state.StepResultsByIndex.TryGetValue(step.Index, out var result) &&
                                                      result.Success && result.Message?.StartsWith("skipped (", StringComparison.Ordinal) != true))
            writer.WriteStringValue(DescribeSplitPrepareStep(step));
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Reads and removes this workspace's prepare receipt; returns the steps it proves already ran here.</summary>
    private static HashSet<int> ConsumeSplitPrepareReceipt(PipelineRunState state)
    {
        var path = GetSplitPrepareReceiptPath(state);
        if (!File.Exists(path))
        {
            if (state.SplitPrepareSteps.Count > 0)
                state.Logger?.Info($"Split pipeline: no prepare receipt in this workspace; splitPhase prepare steps run here.");
            return new HashSet<int>();
        }

        var current = state.Steps.Where(step => state.SplitPrepareSteps.Contains(step.Index))
            .ToDictionary(DescribeSplitPrepareStep, static step => step.Index, StringComparer.Ordinal);
        var ran = new HashSet<int>();
        string? mismatch;
        try
        {
            using (var document = JsonDocument.Parse(File.ReadAllBytes(path)))
            {
                var root = document.RootElement;
                var steps = GetArrayOfStrings(root, "steps") ?? Array.Empty<string>();
                mismatch = GetInt(root, "version") != 1 ? "version"
                    : !string.Equals(GetString(root, "source"), ResolveSplitSourceIdentity(state.BaseDir), StringComparison.Ordinal) ? "source commit"
                    : !string.Equals(GetString(root, "engine"), ResolveSplitEngineIdentity(), StringComparison.Ordinal) ? "engine"
                    : steps.Any(step => !current.ContainsKey(step)) ? "steps"
                    : null;
                if (mismatch is null)
                    ran.UnionWith(steps.Select(step => current[step]));
            }
        }
        catch (JsonException ex)
        {
            mismatch = ex.Message;
        }
        File.Delete(path);

        if (mismatch is not null)
        {
            state.Logger?.Info($"Split pipeline: prepare receipt does not match this run ({mismatch}); splitPhase prepare steps run here.");
            return new HashSet<int>();
        }
        return ran;
    }

    private static HashSet<string> GetSplitExcludedPaths(PipelineRunState state)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in new[] { state.CachePath, state.ProfilePath, state.Split?.ArchivePath })
            if (!string.IsNullOrWhiteSpace(path))
                excluded.Add(Path.GetFullPath(path));
        return excluded;
    }

    private static Dictionary<string, (long Length, long Ticks)> CaptureSplitSnapshot(string root, HashSet<string> excluded)
    {
        var snapshot = new Dictionary<string, (long Length, long Ticks)>(StringComparer.Ordinal);
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
            return snapshot;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = 0 };
        var rootDirectory = new DirectoryInfo(fullRoot);
        var files = rootDirectory.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 })
            .Concat(rootDirectory.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 })
                .Where(static directory => !directory.Name.Equals(".git", StringComparison.Ordinal))
                .SelectMany(directory => directory.EnumerateFiles("*", options)));
        foreach (var file in files)
        {
            if (excluded.Contains(file.FullName))
                continue;
            var relative = Path.GetRelativePath(fullRoot, file.FullName).Replace('\\', '/');
            snapshot[relative] = (file.Length, file.LastWriteTimeUtc.Ticks);
        }
        return snapshot;
    }

    /// <summary>Writes the files this shard added or changed during the apidocs block, plus deletions and its units.</summary>
    private static void WriteSplitArchive(PipelineRunState state)
    {
        var split = state.Split!;
        var plan = EnsureSplitPlan(state);
        var before = plan.Before!;
        var after = CaptureSplitSnapshot(state.BaseDir, GetSplitExcludedPaths(state));
        var changed = after
            .Where(entry => !before.TryGetValue(entry.Key, out var previous) || previous != entry.Value)
            .Select(static entry => entry.Key)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var deleted = before.Keys.Where(path => !after.ContainsKey(path)).OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        var units = plan.Units.Where(unit => unit.Shard == split.ShardIndex).Select(static unit => unit.Key).ToArray();

        var root = Path.GetFullPath(state.BaseDir);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in changed)
            hashes[path] = ComputeFileSha256(Path.Combine(root, path));

        var directory = Path.GetDirectoryName(split.ArchivePath!);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        using (var stream = File.Create(split.ArchivePath!))
        using (var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: false))
        {
            var manifest = SerializeSplitManifest(new SplitManifest
            {
                ShardIndex = split.ShardIndex,
                ShardCount = split.ShardCount,
                Units = units,
                Files = hashes,
                Deleted = deleted,
                Source = ResolveSplitSourceIdentity(state.BaseDir),
                Engine = ResolveSplitEngineIdentity()
            });
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, SplitManifestEntryName)
            {
                DataStream = new MemoryStream(manifest)
            };
            writer.WriteEntry(manifestEntry);
            foreach (var path in changed)
                writer.WriteEntry(Path.Combine(root, path), SplitFileEntryPrefix + path);
        }

        state.Logger?.Info($"Split pipeline: shard {split.ShardIndex}/{split.ShardCount} archived {changed.Length} file(s) and {deleted.Length} deletion(s) to {split.ArchivePath}.");
    }

    /// <summary>
    /// Validates that the shard archives cover every apidocs unit exactly once and agree on shared files,
    /// then applies them to the website root.
    /// </summary>
    private static void ApplySplitArchives(PipelineRunState state)
    {
        var split = state.Split!;
        if (!Directory.Exists(split.ArchivesDirectory))
            throw new InvalidOperationException($"Split archive directory not found: {split.ArchivesDirectory}");

        var archives = Directory.EnumerateFiles(split.ArchivesDirectory!, "*.tar", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path => (Path: path, Manifest: ReadSplitManifest(path)))
            .ToArray();
        if (archives.Length == 0)
            throw new InvalidOperationException($"No split archives (*.tar) found under {split.ArchivesDirectory}.");

        var shardCount = archives[0].Manifest.ShardCount;
        if (archives.Any(archive => archive.Manifest.ShardCount != shardCount))
            throw new InvalidOperationException("Split archives come from runs with different shard counts.");
        var shardIndexes = archives.Select(static archive => archive.Manifest.ShardIndex).OrderBy(static index => index).ToArray();
        if (!shardIndexes.SequenceEqual(Enumerable.Range(1, shardCount)))
            throw new InvalidOperationException($"Expected one split archive for each shard 1..{shardCount}; found shard(s) {string.Join(", ", shardIndexes)}.");

        // Archives from another commit or engine could carry stale documentation with the same unit keys.
        var source = ResolveSplitSourceIdentity(state.BaseDir);
        var engine = ResolveSplitEngineIdentity();
        var foreign = archives.FirstOrDefault(archive => !archive.Manifest.Source.Equals(source, StringComparison.Ordinal) ||
                                                         !archive.Manifest.Engine.Equals(engine, StringComparison.Ordinal));
        if (foreign.Path is not null)
            throw new InvalidOperationException(
                $"Split archive {foreign.Path} was built from source '{foreign.Manifest.Source}' with engine '{foreign.Manifest.Engine}', but this run uses source '{source}' with engine '{engine}'.");

        var plan = EnsureSplitPlan(state);
        var expected = plan.Units.Select(static unit => unit.Key).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in archives.SelectMany(static archive => archive.Manifest.Units))
        {
            if (!expected.Contains(unit))
                throw new InvalidOperationException($"Split archive contains unknown apidocs unit '{unit}'; shards must run the same pipeline.");
            if (!seen.Add(unit))
                throw new InvalidOperationException($"Apidocs unit '{unit}' ran in more than one shard.");
        }
        var missing = expected.Where(unit => !seen.Contains(unit)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"No shard ran apidocs unit(s): {string.Join(", ", missing.Take(5))}{(missing.Length > 5 ? $" (+{missing.Length - 5} more)" : string.Empty)}.");

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, manifest) in archives)
        {
            foreach (var (file, hash) in manifest.Files)
            {
                if (files.TryGetValue(file, out var existing) && !existing.Equals(hash, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Shards produced different content for '{file}'.");
                files[file] = hash;
            }
        }
        var deleted = archives.SelectMany(static archive => archive.Manifest.Deleted).ToHashSet(StringComparer.Ordinal);
        var both = deleted.FirstOrDefault(files.ContainsKey);
        if (both is not null)
            throw new InvalidOperationException($"One shard deleted '{both}' while another produced it.");

        // Check every entry, payload hash and target first, so a bad archive leaves the site untouched.
        var root = Path.GetFullPath(state.BaseDir);
        foreach (var (archivePath, manifest) in archives)
            VerifySplitArchive(archivePath, manifest, root);
        foreach (var path in deleted)
            ResolveSplitTarget(root, path);

        var written = 0;
        foreach (var (archivePath, manifest) in archives)
            written += ExtractSplitArchive(archivePath, manifest, root);
        foreach (var path in deleted)
        {
            var target = ResolveSplitTarget(root, path);
            if (File.Exists(target))
                File.Delete(target);
        }

        state.Logger?.Info($"Split pipeline: merged {archives.Length} shard archive(s): {files.Count} file(s), {written} written, {deleted.Count} deletion(s).");
    }

    private static void VerifySplitArchive(string archivePath, SplitManifest manifest, string root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var stream = File.OpenRead(archivePath);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.Name.Equals(SplitManifestEntryName, StringComparison.Ordinal))
                continue;
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
                !entry.Name.StartsWith(SplitFileEntryPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Split archive {archivePath} contains unexpected entry '{entry.Name}'.");

            var relative = entry.Name.Substring(SplitFileEntryPrefix.Length);
            if (!manifest.Files.TryGetValue(relative, out var hash) || !seen.Add(relative))
                throw new InvalidOperationException($"Split archive {archivePath} contains '{relative}' outside its manifest or more than once.");
            ResolveSplitTarget(root, relative);
            var actual = entry.DataStream is null
                ? Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant()
                : Convert.ToHexString(SHA256.HashData(entry.DataStream)).ToLowerInvariant();
            if (!actual.Equals(hash, StringComparison.Ordinal))
                throw new InvalidOperationException($"Split archive {archivePath} has different content for '{relative}' than its manifest records.");
        }

        var missing = manifest.Files.Keys.FirstOrDefault(path => !seen.Contains(path));
        if (missing is not null)
            throw new InvalidOperationException($"Split archive {archivePath} does not contain '{missing}' listed in its manifest.");
    }

    private static int ExtractSplitArchive(string archivePath, SplitManifest manifest, string root)
    {
        var written = 0;
        using var stream = File.OpenRead(archivePath);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.Name.Equals(SplitManifestEntryName, StringComparison.Ordinal))
                continue;
            var relative = entry.Name.Substring(SplitFileEntryPrefix.Length);
            var hash = manifest.Files[relative];
            var target = ResolveSplitTarget(root, relative);
            // A file already in place (this machine's own shard, or a shared file) is left untouched.
            if (File.Exists(target) && ComputeFileSha256(target).Equals(hash, StringComparison.Ordinal))
                continue;

            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            entry.ExtractToFile(target, overwrite: true);
            written++;
        }
        return written;
    }

    private static string ResolveSplitTarget(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Split('/', '\\').Any(static segment => segment is "" or "." or ".."))
            throw new InvalidOperationException($"Split archive path '{relative}' must be a plain relative path.");
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Split archive path '{relative}' resolves outside the pipeline root.");
        // Linked folders inside the root must not redirect writes or deletions elsewhere.
        if (!IsUnderSplitRoot(ResolveSplitPhysicalPath(target), ResolveSplitPhysicalPath(root)))
            throw new InvalidOperationException($"Split archive path '{relative}' resolves through a link outside the pipeline root.");
        return target;
    }

    // Case-sensitive except on Windows, so a case-variant sibling of the root is outside it.
    private static bool IsUnderSplitRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(path, normalizedRoot, FileSystemPathComparison) ||
               path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, FileSystemPathComparison);
    }

    /// <summary>Resolves every link on the path, including links inside link targets.</summary>
    private static string ResolveSplitPhysicalPath(string path, int depth = 0)
    {
        if (depth > 40)
            throw new InvalidOperationException($"Too many levels of links while resolving '{path}'.");
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (var segment in full.Substring(current.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo? info = Directory.Exists(next) ? new DirectoryInfo(next) : File.Exists(next) ? new FileInfo(next) : null;
            if (info?.LinkTarget is { } target)
                next = ResolveSplitPhysicalPath(Path.IsPathRooted(target) ? target : Path.Combine(current, target), depth + 1);
            current = next;
        }
        return current;
    }

    /// <summary>The commit checked out at the pipeline root, or empty outside a Git work tree.</summary>
    private static string ResolveSplitSourceIdentity(string baseDir)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = baseDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "rev-parse", "--verify", "HEAD" })
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null)
                return string.Empty;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.ToLowerInvariant() : string.Empty;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>The engine website-runner selected, or this engine's own build identity when run directly.</summary>
    private static string ResolveSplitEngineIdentity()
    {
        var identity = Environment.GetEnvironmentVariable("POWERFORGE_ENGINE_IDENTITY");
        return string.IsNullOrWhiteSpace(identity) ? PipelineToolFingerprint : identity.Trim();
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static byte[] SerializeSplitManifest(SplitManifest manifest)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteNumber("shardIndex", manifest.ShardIndex);
            writer.WriteNumber("shardCount", manifest.ShardCount);
            writer.WriteString("source", manifest.Source);
            writer.WriteString("engine", manifest.Engine);
            writer.WriteStartArray("units");
            foreach (var unit in manifest.Units)
                writer.WriteStringValue(unit);
            writer.WriteEndArray();
            writer.WriteStartObject("files");
            foreach (var (path, hash) in manifest.Files.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
                writer.WriteString(path, hash);
            writer.WriteEndObject();
            writer.WriteStartArray("deleted");
            foreach (var path in manifest.Deleted)
                writer.WriteStringValue(path);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static SplitManifest ReadSplitManifest(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        using var reader = new TarReader(stream);
        var entry = reader.GetNextEntry();
        if (entry is null || !entry.Name.Equals(SplitManifestEntryName, StringComparison.Ordinal) || entry.DataStream is null)
            throw new InvalidOperationException($"{archivePath} is not a split pipeline archive.");

        using var document = JsonDocument.Parse(entry.DataStream);
        var root = document.RootElement;
        if (GetInt(root, "version") != 1)
            throw new InvalidOperationException($"{archivePath} has an unsupported split manifest version.");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("files", out var filesElement) && filesElement.ValueKind == JsonValueKind.Object)
            foreach (var property in filesElement.EnumerateObject())
                files[property.Name] = property.Value.GetString() ?? string.Empty;
        return new SplitManifest
        {
            ShardIndex = GetInt(root, "shardIndex") ?? 0,
            ShardCount = GetInt(root, "shardCount") ?? 0,
            Units = GetArrayOfStrings(root, "units") ?? Array.Empty<string>(),
            Files = files,
            Deleted = GetArrayOfStrings(root, "deleted") ?? Array.Empty<string>(),
            Source = GetString(root, "source") ?? string.Empty,
            Engine = GetString(root, "engine") ?? string.Empty
        };
    }

    /// <summary>Keeps the prepare run's step results in the finish profile so it covers the whole pipeline.</summary>
    private static void MergeSplitPrepareProfile(PipelineRunState state)
    {
        if (state.Split?.Phase != WebPipelineSplitPhase.Finish || string.IsNullOrWhiteSpace(state.ProfilePath) || !File.Exists(state.ProfilePath))
            return;
        try
        {
            var prior = JsonSerializer.Deserialize(File.ReadAllText(state.ProfilePath), WebCliJson.Context.WebPipelineResult);
            var result = state.Result;
            if (prior?.Steps is not { } priorSteps || priorSteps.Count != state.TotalSteps || result.Steps.Count != state.TotalSteps)
                return;
            for (var index = 0; index < state.SplitLastIndex; index++)
                result.Steps[index] = priorSteps[index];
            foreach (var stepIndex in state.SplitPreparedHere)
                result.Steps[stepIndex - 1] = priorSteps[stepIndex - 1];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            state.Logger?.Warn($"Split pipeline: prepare profile was not merged: {ex.Message}");
        }
    }
}
