using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    // Task types whose execution only reads earlier outputs and writes its own declared output roots.
    private static readonly HashSet<string> ParallelSafeTasks = new(StringComparer.OrdinalIgnoreCase)
    {
        "apidocs"
    };

    // Paths an apidocs run reads; a unit reading another unit's output must run after it.
    private static readonly string[][] ApiDocsReadKeys =
    {
        new[] { "xml" }, new[] { "help", "helpPath", "help-path" }, new[] { "assembly" },
        new[] { "headHtml", "head-html" }, new[] { "headerHtml", "header-html" }, new[] { "footerHtml", "footer-html" },
        new[] { "templateRoot", "template-root" }, new[] { "templateIndex", "template-index" }, new[] { "templateType", "template-type" },
        new[] { "templateDocsIndex", "template-docs-index" }, new[] { "templateDocsType", "template-docs-type" },
        new[] { "docsScript", "docs-script" }, new[] { "searchScript", "search-script" },
        new[] { "criticalCssPath", "critical-css-path", "criticalCss", "critical-css" },
        new[] { "nav", "navJson", "nav-json" }, new[] { "config" },
        new[] { "psExamplesPath", "ps-examples-path", "powerShellExamplesPath", "powershell-examples-path" },
        new[] { "psManifestPath", "ps-manifest-path", "powerShellManifestPath", "powershell-manifest-path" },
        new[] { "psCommandMetadataPath", "ps-command-metadata-path", "powerShellCommandMetadataPath", "powershell-command-metadata-path" },
        new[] { "relatedContentManifest", "related-content-manifest", "exampleManifest", "example-manifest" }
    };

    private static readonly string[] ApiDocsReadArrayKeys =
    {
        "xmls", "xmlPaths", "assemblies", "assemblyPaths",
        "relatedContentManifests", "related-content-manifests", "exampleManifests", "example-manifests"
    };

    private static readonly string[][] ApiDocsExtraOutputKeys =
    {
        new[] { "coverageReport", "coverage-report", "coverageReportPath", "coverage-report-path" },
        new[] { "xrefMap", "xref-map", "xrefMapPath", "xref-map-path" },
        new[] { "powerShellExampleValidationReport", "powershell-example-validation-report",
            "powerShellExampleValidationReportPath", "powershell-example-validation-report-path" }
    };

    private static readonly AsyncLocal<int> CurrentPipelineMaxParallelism = new();

    /// <summary>One generator run: what it writes, what it reads, and the API route it owns.</summary>
    private sealed class ApiDocsFootprint
    {
        public int Owner { get; init; }
        public List<string> Outputs { get; } = new();
        public List<string> Reads { get; } = new();
        public string BaseUrl { get; init; } = string.Empty;
    }

    /// <summary>
    /// Reads the opt-in pipeline-level <c>maxParallelism</c> setting. Omitting it, or 1, keeps sequential execution.
    /// </summary>
    private static int GetPipelineMaxParallelism(JsonElement root)
    {
        var value = GetInt(root, "maxParallelism") ?? 1;
        return Math.Clamp(value, 1, 64);
    }

    /// <summary>
    /// Runs the pipeline in pipeline order, letting consecutive parallel-safe steps whose dependencies are
    /// satisfied overlap. Every other step is a barrier: it starts after all earlier steps finished and later
    /// steps wait for it. Output of overlapping steps is buffered and printed in pipeline order.
    /// </summary>
    private static PipelineStepOutcome? RunStepsInParallelGroups(PipelineRunState state, int maxParallelism)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var previousParallelism = CurrentPipelineMaxParallelism.Value;
        Console.SetOut(new PipelineConsoleRouter(originalOut, isError: false));
        Console.SetError(new PipelineConsoleRouter(originalError, isError: true));
        CurrentPipelineMaxParallelism.Value = maxParallelism;
        try
        {
            var position = 0;
            while (position < state.Steps.Count)
            {
                var group = CollectParallelGroup(state, position);
                position += group.Count;
                var failure = group.Count == 1
                    ? RunPipelineStep(state, group[0]) is { Failed: true } single ? single : null
                    : RunParallelGroup(state, group, maxParallelism);
                if (failure is not null)
                    return failure;
            }
            return null;
        }
        finally
        {
            CurrentPipelineMaxParallelism.Value = previousParallelism;
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static List<PipelineStepDefinition> CollectParallelGroup(PipelineRunState state, int start)
    {
        var first = state.Steps[start];
        var group = new List<PipelineStepDefinition> { first };
        var footprints = TryGetParallelStepFootprints(state.BaseDir, first);
        if (footprints is null)
            return group;

        for (var index = start + 1; index < state.Steps.Count; index++)
        {
            var candidate = state.Steps[index];
            var candidateFootprints = TryGetParallelStepFootprints(state.BaseDir, candidate);
            // A declared dependency orders the read; anything else keeps sequential order.
            if (candidateFootprints is null ||
                candidateFootprints.Any(unit => footprints.Any(existing =>
                    Conflicts(existing, unit, unitDependsOnExisting: candidate.DependencyIndexes.Contains(existing.Owner)))))
                break;

            footprints.AddRange(candidateFootprints);
            group.Add(candidate);
        }
        return group;
    }

    private static List<ApiDocsFootprint>? TryGetParallelStepFootprints(string baseDir, PipelineStepDefinition definition)
    {
        if (!ParallelSafeTasks.Contains(definition.Task))
            return null;
        return TryGetApiDocsFootprints(baseDir, definition.Element, definition.Index);
    }

    /// <summary>
    /// Describes each generator run of an apidocs step (one per batch input) from the same merged
    /// step that execution uses. Returns null when the step must not overlap with other work.
    /// </summary>
    private static List<ApiDocsFootprint>? TryGetApiDocsFootprints(string baseDir, JsonElement step, int owner)
    {
        var footprints = new List<ApiDocsFootprint>();
        var inputs = GetArrayOfObjects(step, "inputs") ?? GetArrayOfObjects(step, "entries");
        try
        {
            if (inputs is not { Length: > 0 })
                return TryAddApiDocsFootprint(baseDir, step, owner, footprints) ? footprints : null;

            foreach (var input in inputs)
            {
                using var merged = CreateMergedApiDocsStepDocument(step, input);
                if (!TryAddApiDocsFootprint(baseDir, merged.RootElement, owner, footprints))
                    return null;
            }
            return footprints;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or
                                   UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            // Unresolvable configuration runs as a normal step, so its usual skip and error handling apply.
            return null;
        }
    }

    private static bool TryAddApiDocsFootprint(string baseDir, JsonElement step, int owner, List<ApiDocsFootprint> footprints)
    {
        // Example scripts run with their own folder as the working directory and may share files there.
        if (GetBool(step, "executePowerShellExamples") == true || GetBool(step, "execute-powershell-examples") == true ||
            GetBool(step, "failOnPowerShellExampleExecution") == true || GetBool(step, "fail-on-powershell-example-execution") == true)
            return false;

        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        if (string.IsNullOrWhiteSpace(outPath))
            return false;

        var footprint = new ApiDocsFootprint
        {
            Owner = owner,
            BaseUrl = (GetString(step, "baseUrl") ?? GetString(step, "base-url") ?? "/api").Trim().TrimEnd('/')
        };
        footprint.Outputs.Add(ResolvePhysicalPath(outPath));
        foreach (var names in ApiDocsExtraOutputKeys)
        {
            var path = ResolvePath(baseDir, FirstString(step, names));
            if (!string.IsNullOrWhiteSpace(path))
                footprint.Outputs.Add(ResolvePhysicalPath(path));
        }
        foreach (var names in ApiDocsReadKeys)
        {
            var path = ResolvePath(baseDir, FirstString(step, names));
            if (!string.IsNullOrWhiteSpace(path))
                footprint.Reads.Add(ResolvePhysicalPath(path));
        }
        foreach (var name in ApiDocsReadArrayKeys)
        {
            foreach (var value in GetArrayOfStrings(step, name) ?? Array.Empty<string>())
            {
                var path = ResolvePath(baseDir, value);
                if (!string.IsNullOrWhiteSpace(path))
                    footprint.Reads.Add(ResolvePhysicalPath(path));
            }
        }
        footprints.Add(footprint);
        return true;
    }

    private static string? FirstString(JsonElement step, string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(step, name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    /// <summary>True when two generator runs could observe or overwrite each other's files.</summary>
    private static bool Conflicts(ApiDocsFootprint existing, ApiDocsFootprint candidate, bool unitDependsOnExisting)
    {
        if (string.Equals(existing.BaseUrl, candidate.BaseUrl, StringComparison.OrdinalIgnoreCase))
            return true; // Same routes share social card and alias file names.
        if (existing.Outputs.Any(left => candidate.Outputs.Any(right => PathsOverlap(left, right))))
            return true;
        if (existing.Reads.Any(read => candidate.Outputs.Any(output => IsUnder(read, output))))
            return true;
        return !unitDependsOnExisting &&
               candidate.Reads.Any(read => existing.Outputs.Any(output => IsUnder(read, output)));
    }

    /// <summary>True when batch inputs can run concurrently without seeing each other's files.</summary>
    private static bool AreIndependentApiDocsInputs(string baseDir, JsonElement step)
    {
        var footprints = TryGetApiDocsFootprints(baseDir, step, owner: 0);
        if (footprints is null)
            return false;
        for (var left = 0; left < footprints.Count; left++)
            for (var right = left + 1; right < footprints.Count; right++)
                if (Conflicts(footprints[left], footprints[right], unitDependsOnExisting: false))
                    return false;
        return true;
    }

    /// <summary>Resolves links in every existing segment so two spellings of one folder compare equal.</summary>
    private static string ResolvePhysicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        foreach (var segment in full.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            for (var hops = 0; hops < 32; hops++)
            {
                FileSystemInfo? info = Directory.Exists(next) ? new DirectoryInfo(next) : File.Exists(next) ? new FileInfo(next) : null;
                if (info?.LinkTarget is not { } target)
                    break;
                next = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(Path.GetDirectoryName(next) ?? current, target));
            }
            current = next;
        }
        return current;
    }

    // Case-insensitive on every host: case variants on case-insensitive volumes name one folder.
    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsOverlap(string left, string right) => IsUnder(left, right) || IsUnder(right, left);

    private static PipelineStepOutcome? RunParallelGroup(PipelineRunState state, List<PipelineStepDefinition> group, int maxParallelism)
    {
        var groupIndexes = new HashSet<int>(group.Select(static step => step.Index));
        var captures = new PipelineOutputCapture?[group.Count];
        var tasks = new Task<PipelineStepOutcome>?[group.Count];
        var finished = new bool[group.Count];
        var completedIndexes = new HashSet<int>();
        PipelineStepOutcome? failure = null;
        ExceptionDispatchInfo? unexpected = null;
        var running = 0;
        var nextToFlush = 0;

        state.DeferResultOrdering = true;
        try
        {
            while (true)
            {
                if (failure is null && unexpected is null)
                {
                    for (var position = 0; position < group.Count && running < maxParallelism; position++)
                    {
                        if (tasks[position] is not null)
                            continue;
                        var definition = group[position];
                        // Dependencies inside the group must finish first; earlier barriers are already complete.
                        if (definition.DependencyIndexes.Any(index => groupIndexes.Contains(index) && !completedIndexes.Contains(index)))
                            continue;

                        var capture = new PipelineOutputCapture();
                        captures[position] = capture;
                        tasks[position] = Task.Run(() => capture.Run(() => RunPipelineStep(state, definition)));
                        running++;
                    }
                }

                if (running == 0)
                    break;

                var active = Enumerable.Range(0, group.Count)
                    .Where(position => tasks[position] is not null && !finished[position])
                    .ToArray();
                var completed = active[Task.WaitAny(active.Select(position => (Task)tasks[position]!).ToArray())];
                finished[completed] = true;
                running--;
                completedIndexes.Add(group[completed].Index);
                var task = tasks[completed]!;
                if (task.IsFaulted)
                {
                    unexpected ??= ExceptionDispatchInfo.Capture(task.Exception!.InnerExceptions.Count == 1
                        ? task.Exception.InnerExceptions[0]
                        : task.Exception);
                }
                else if (task.Result.Failed)
                {
                    failure ??= task.Result;
                }

                // Publish logs and results in pipeline order as soon as the prefix is complete.
                while (nextToFlush < group.Count && finished[nextToFlush])
                {
                    captures[nextToFlush]!.ReplayTo(Console.Out, Console.Error);
                    state.PublishDeferredResults(group[nextToFlush].Index);
                    nextToFlush++;
                }
            }

            // Steps that were never started (after a failure) produce no output, as in sequential runs.
            while (nextToFlush < group.Count)
            {
                if (finished[nextToFlush])
                {
                    captures[nextToFlush]!.ReplayTo(Console.Out, Console.Error);
                    state.PublishDeferredResults(group[nextToFlush].Index);
                }
                nextToFlush++;
            }
        }
        finally
        {
            // Never leave started steps running past this group, even if replaying output failed.
            var started = tasks.Where(static task => task is not null).Select(static task => (Task)task!).ToArray();
            try { Task.WaitAll(started); }
            catch (AggregateException) { /* already recorded from the completed task */ }
            state.PublishDeferredResults(int.MaxValue);
            state.DeferResultOrdering = false;
        }

        unexpected?.Throw();
        return failure;
    }

    /// <summary>
    /// Runs independent work items in parallel when the pipeline enabled parallelism, replaying each
    /// item's console output in item order. Returns false when the caller should run sequentially.
    /// </summary>
    private static bool TryRunInParallelWithOrderedOutput<T>(IReadOnlyList<T> items, Action<T, int> body)
    {
        var maxParallelism = CurrentPipelineMaxParallelism.Value;
        // Only set while RunStepsInParallelGroups has the console router installed.
        if (maxParallelism <= 1 || items.Count <= 1)
            return false;

        var captures = new PipelineOutputCapture[items.Count];
        var errors = new Exception?[items.Count];
        for (var index = 0; index < items.Count; index++)
            captures[index] = new PipelineOutputCapture();

        var failed = 0;
        Parallel.For(0, items.Count, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism }, index =>
        {
            // Like the sequential loop, start nothing new once an item failed.
            if (Volatile.Read(ref failed) != 0)
                return;
            try
            {
                captures[index].Run(() => body(items[index], index));
            }
            catch (Exception ex)
            {
                errors[index] = ex;
                Interlocked.Exchange(ref failed, 1);
            }
        });

        // Replay everything that ran before reporting the first failure in item order.
        foreach (var capture in captures)
            capture.ReplayTo(Console.Out, Console.Error);
        if (errors.FirstOrDefault(static error => error is not null) is { } firstError)
            ExceptionDispatchInfo.Capture(firstError).Throw();
        return true;
    }

    /// <summary>Buffers console output written by one pipeline step (or batch item) on any thread of its flow.</summary>
    private sealed class PipelineOutputCapture
    {
        private readonly object _gate = new();
        private readonly List<(bool IsError, string Text)> _segments = new();

        internal static readonly AsyncLocal<PipelineOutputCapture?> Current = new();

        public T Run<T>(Func<T> action)
        {
            var previous = Current.Value;
            Current.Value = this;
            try
            {
                return action();
            }
            finally
            {
                Current.Value = previous;
            }
        }

        public void Run(Action action) => Run<bool>(() =>
        {
            action();
            return true;
        });

        public void Append(bool isError, string? text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            lock (_gate)
                _segments.Add((isError, text));
        }

        public void ReplayTo(TextWriter output, TextWriter error)
        {
            lock (_gate)
            {
                foreach (var (isError, text) in _segments)
                    (isError ? error : output).Write(text);
                _segments.Clear();
            }
            output.Flush();
            error.Flush();
        }
    }

    /// <summary>Routes console writes to the active step capture; writes outside a step go straight through.</summary>
    private sealed class PipelineConsoleRouter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly bool _isError;

        public PipelineConsoleRouter(TextWriter inner, bool isError)
        {
            _inner = inner;
            _isError = isError;
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value) => Write(value.ToString());

        public override void Write(string? value)
        {
            if (PipelineOutputCapture.Current.Value is { } capture)
                capture.Append(_isError, value);
            else
                lock (_inner) _inner.Write(value);
        }

        public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));

        public override void WriteLine(string? value) => Write((value ?? string.Empty) + CoreNewLineStr);

        public override void WriteLine() => Write(CoreNewLineStr);

        public override void Flush()
        {
            if (PipelineOutputCapture.Current.Value is null)
                lock (_inner) _inner.Flush();
        }

        private string CoreNewLineStr => new string(CoreNewLine);
    }
}
