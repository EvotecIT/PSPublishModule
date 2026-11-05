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

    private static readonly AsyncLocal<int> CurrentPipelineMaxParallelism = new();

    /// <summary>
    /// Reads the opt-in pipeline-level <c>maxParallelism</c> setting. Values of 1 or less keep sequential execution.
    /// </summary>
    private static int GetPipelineMaxParallelism(JsonElement root)
    {
        var value = GetInt(root, "maxParallelism") ?? GetInt(root, "max-parallelism") ?? 1;
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
        var group = new List<PipelineStepDefinition> { state.Steps[start] };
        var first = state.Steps[start];
        var outputs = new List<string>();
        if (!TryGetParallelStepOutputs(state.BaseDir, first, outputs))
            return group;

        for (var index = start + 1; index < state.Steps.Count; index++)
        {
            var candidate = state.Steps[index];
            var candidateOutputs = new List<string>();
            if (!TryGetParallelStepOutputs(state.BaseDir, candidate, candidateOutputs) ||
                candidateOutputs.Any(output => outputs.Any(existing => PathsOverlap(existing, output))))
                break;

            outputs.AddRange(candidateOutputs);
            group.Add(candidate);
        }
        return group;
    }

    private static bool TryGetParallelStepOutputs(string baseDir, PipelineStepDefinition definition, List<string> outputs)
    {
        if (!ParallelSafeTasks.Contains(definition.Task))
            return false;
        return TryCollectApiDocsOutputs(baseDir, definition.Element, outputs);
    }

    private static bool TryCollectApiDocsOutputs(string baseDir, JsonElement step, List<string> outputs)
    {
        var inputs = GetArrayOfObjects(step, "inputs") ?? GetArrayOfObjects(step, "entries");
        if (inputs is { Length: > 0 })
        {
            foreach (var input in inputs)
            {
                if (!TryAddApiDocsOutputs(baseDir, input, step, outputs))
                    return false;
            }
            return true;
        }
        return TryAddApiDocsOutputs(baseDir, step, null, outputs);
    }

    private static bool TryAddApiDocsOutputs(string baseDir, JsonElement step, JsonElement? parent, List<string> outputs)
    {
        string? Read(params string[] names)
        {
            foreach (var name in names)
            {
                var value = GetString(step, name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            if (parent is { } parentStep)
            {
                foreach (var name in names)
                {
                    var value = GetString(parentStep, name);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
            return null;
        }

        var outPath = Read("out", "output");
        if (string.IsNullOrWhiteSpace(outPath))
            return false;
        var candidates = new List<string?>
        {
            outPath,
            Read("coverageReport", "coverage-report", "coverageReportPath", "coverage-report-path"),
            Read("xrefMap", "xref-map", "xrefMapPath", "xref-map-path"),
            Read("powerShellExampleValidationReport", "powershell-example-validation-report",
                "powerShellExampleValidationReportPath", "powershell-example-validation-report-path")
        };
        foreach (var candidate in candidates)
        {
            var resolved = ResolvePath(baseDir, candidate);
            if (string.IsNullOrWhiteSpace(resolved))
                continue;
            var full = Path.GetFullPath(resolved);
            if (outputs.Any(existing => PathsOverlap(existing, full) && !IsUnder(full, Path.GetFullPath(ResolvePath(baseDir, outPath)!))))
                return false;
            outputs.Add(full);
        }
        return true;
    }

    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(path, normalizedRoot, FileSystemPathComparison) ||
               path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, FileSystemPathComparison);
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

        Parallel.For(0, items.Count, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism }, index =>
        {
            try
            {
                captures[index].Run(() => body(items[index], index));
            }
            catch (Exception ex)
            {
                errors[index] = ex;
            }
        });

        for (var index = 0; index < items.Count; index++)
        {
            captures[index].ReplayTo(Console.Out, Console.Error);
            if (errors[index] is { } error)
                ExceptionDispatchInfo.Capture(error).Throw();
        }
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
