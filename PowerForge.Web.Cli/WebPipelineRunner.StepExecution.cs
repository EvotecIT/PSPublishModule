using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private sealed class PipelineRunState
    {
        public required List<PipelineStepDefinition> Steps { get; init; }
        public required int TotalSteps { get; init; }
        public required string BaseDir { get; init; }
        public required bool Fast { get; init; }
        public required string EffectiveMode { get; init; }
        public HashSet<string>? OnlyTaskSet { get; init; }
        public HashSet<string>? SkipTaskSet { get; init; }
        public WebConsoleLogger? Logger { get; init; }
        public required bool ProfileEnabled { get; init; }
        public required bool ProfileWriteOnFail { get; init; }
        public string? ProfilePath { get; init; }
        public required bool CacheEnabled { get; init; }
        public string? CachePath { get; init; }
        public WebPipelineCacheState? CacheState { get; init; }
        public required HashSet<string> PipelineSourcePaths { get; init; }
        public required JsonElement[] EnabledIndexNowSteps { get; init; }
        public required WebPipelineResult Result { get; init; }

        public object Gate { get; } = new();
        public Dictionary<int, WebPipelineStepResult> StepResultsByIndex { get; } = new();
        public Dictionary<string, string[]> CacheOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool CacheUpdated { get; set; }

        // Allows fast mode to scope optimize/audit to just the pages touched by the last build step.
        public string LastBuildOutPath { get; set; } = string.Empty;
        public string[] LastBuildUpdatedFiles { get; set; } = Array.Empty<string>();

        // Parallel groups record results here and publish them in pipeline order.
        public bool DeferResultOrdering { get; set; }
        public SortedDictionary<int, WebPipelineStepResult> DeferredResults { get; } = new();

        public void Commit(int stepIndex, WebPipelineStepResult stepResult)
        {
            lock (Gate)
            {
                StepResultsByIndex[stepIndex] = stepResult;
                if (DeferResultOrdering)
                    DeferredResults[stepIndex] = stepResult;
                else
                    Result.Steps.Add(stepResult);
            }
        }

        public void PublishDeferredResults(int throughIndexInclusive)
        {
            lock (Gate)
            {
                foreach (var index in DeferredResults.Keys.Where(index => index <= throughIndexInclusive).ToArray())
                {
                    Result.Steps.Add(DeferredResults[index]);
                    DeferredResults.Remove(index);
                }
            }
        }
    }

    private sealed class PipelineStepOutcome
    {
        public bool Failed { get; init; }
        public bool FailureProfileOutputIsSafe { get; init; } = true;
    }

    private static readonly PipelineStepOutcome CompletedStepOutcome = new();

    private static PipelineStepOutcome RunPipelineStep(PipelineRunState state, PipelineStepDefinition definition)
    {
        var step = definition.Element;
        var task = definition.Task;
        var stepIndex = definition.Index;
        var baseDir = state.BaseDir;
        var logger = state.Logger;
        var label = $"[{stepIndex}/{state.TotalSteps}] {task}";
        var stepResult = new WebPipelineStepResult { Task = task };

        var skipReason = GetSkipReason(task, step, state.EffectiveMode, state.OnlyTaskSet, state.SkipTaskSet);
        if (skipReason is not null)
        {
            stepResult.Success = true;
            stepResult.Cached = false;
            stepResult.DurationMs = 0;
            stepResult.Message = $"skipped ({skipReason})";
            state.Commit(stepIndex, stepResult);
            if (state.ProfileEnabled)
                logger?.Info($"Finished {label} (skipped) in 0 ms");
            return CompletedStepOutcome;
        }

        logger?.Info($"Starting {label}...");
        var stopwatch = Stopwatch.StartNew();
        var cacheKey = $"{stepIndex}:{task}";
        var stepFingerprint = string.Empty;
        string lastBuildOutPath;
        string[] lastBuildUpdatedFiles;
        lock (state.Gate)
        {
            lastBuildOutPath = state.LastBuildOutPath;
            lastBuildUpdatedFiles = state.LastBuildUpdatedFiles;
        }
        var expectedOutputs = GetExpectedStepOutputs(task, step, baseDir, lastBuildOutPath);
        bool dependencyMiss;
        lock (state.Gate)
        {
            var stepResultsByIndex = state.StepResultsByIndex;
            if (definition.DependencyIndexes.Length > 0)
            {
                foreach (var dependencyIndex in definition.DependencyIndexes)
                {
                    if (!stepResultsByIndex.TryGetValue(dependencyIndex, out var dependencyResult) || !dependencyResult.Success)
                    {
                        throw new InvalidOperationException($"Step '{definition.Id}' dependency #{dependencyIndex} failed or was not executed.");
                    }
                }
            }

            dependencyMiss = definition.DependencyIndexes.Any(index =>
                !stepResultsByIndex.TryGetValue(index, out var dependencyResult) || !dependencyResult.Cached);
            if (state.Steps.Any(prior => prior.Index < stepIndex &&
                                         (prior.Task.Equals("release-hub", StringComparison.OrdinalIgnoreCase) ||
                                          prior.Task.Equals("build", StringComparison.OrdinalIgnoreCase)) &&
                                         stepResultsByIndex.TryGetValue(prior.Index, out var priorResult) &&
                                         priorResult.Success && !priorResult.Cached &&
                                         priorResult.Message?.StartsWith("skipped (", StringComparison.OrdinalIgnoreCase) != true))
            {
                // A release refresh or site build can change data consumed by later
                // verify, audit, and publish preparation without explicit dependencies.
                dependencyMiss = true;
            }
        }
        var cacheStateLocal = state.CacheState;
        var cacheable = state.CacheEnabled && cacheStateLocal is not null && IsCacheableStep(task, step);
        if (cacheable)
        {
            var fingerprintSalt = state.Fast ? $"fast|{PipelineToolFingerprint}" : PipelineToolFingerprint;
            try
            {
                stepFingerprint = ComputeStepFingerprint(baseDir, step, fingerprintSalt);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or BadImageFormatException)
            {
                // Discovery failure must never reuse an old output. Execute the task
                // so its normal validation reports the actionable configuration error.
                cacheable = false;
            }
            WebPipelineCacheEntry? cacheEntry = null;
            var cacheEntryFound = false;
            if (cacheable)
            {
                lock (state.Gate)
                    cacheEntryFound = cacheStateLocal!.Entries.TryGetValue(cacheKey, out cacheEntry);
            }
            if (cacheable && cacheEntryFound &&
                string.Equals(cacheEntry!.Fingerprint, stepFingerprint, StringComparison.Ordinal) &&
                !dependencyMiss &&
                AreExpectedOutputsPresent(expectedOutputs) &&
                (expectedOutputs.Length == 0 ||
                 (ComputeOutputStamp(expectedOutputs) is { } currentOutputStamp &&
                  string.Equals(cacheEntry.OutputStamp, currentOutputStamp, StringComparison.Ordinal))))
            {
                stepResult.Success = true;
                stepResult.Cached = true;
                stepResult.Message = AppendDuration(cacheEntry.Message ?? "cache hit", stopwatch);
                stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
                if (task.Equals("build", StringComparison.OrdinalIgnoreCase))
                {
                    var cachedBuildOut = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                    if (!string.IsNullOrWhiteSpace(cachedBuildOut))
                    {
                        lock (state.Gate)
                            state.LastBuildOutPath = cachedBuildOut;
                    }
                }
                lock (state.Gate)
                    state.CacheOutputs[cacheKey] = expectedOutputs;
                state.Commit(stepIndex, stepResult);
                if (state.ProfileEnabled)
                    logger?.Info($"Finished {label} (cache hit) in {FormatDuration(stopwatch.Elapsed)}");
                return CompletedStepOutcome;
            }
        }

        var failureProfileOutputIsSafe = true;
        try
        {
            if ((task.Equals("build", StringComparison.OrdinalIgnoreCase) ||
                 task.Equals("dotnet-publish", StringComparison.OrdinalIgnoreCase)) &&
                GetBool(step, "clean") == true)
            {
                var cleanOutput = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                if (!string.IsNullOrWhiteSpace(cleanOutput))
                    foreach (var input in state.PipelineSourcePaths)
                        WebOutputPathGuard.ValidateSourceInput(cleanOutput, input);
            }
            // A preceding sitemap step can create index leaves absent during initial validation.
            if (task.Equals("indexnow", StringComparison.OrdinalIgnoreCase))
            {
                failureProfileOutputIsSafe = false;
                ValidateIndexNowOutputPaths(state.EnabledIndexNowSteps, baseDir, state.PipelineSourcePaths,
                    state.ProfileEnabled || state.ProfileWriteOnFail ? state.ProfilePath : null, state.CacheEnabled ? state.CachePath : null);
                failureProfileOutputIsSafe = true;
            }
            var stepLastBuildOutPath = lastBuildOutPath;
            var stepLastBuildUpdatedFiles = lastBuildUpdatedFiles;
            try
            {
                ExecuteTask(task, step, label, baseDir, state.Fast, state.EffectiveMode, logger, ref stepLastBuildOutPath, ref stepLastBuildUpdatedFiles, stepResult);
            }
            finally
            {
                PublishLastBuildState(state, lastBuildOutPath, lastBuildUpdatedFiles, stepLastBuildOutPath, stepLastBuildUpdatedFiles);
            }
        }
        catch (Exception ex)
        {
            stepResult.Success = false;
            var failureMessage = FormatFailureMessage(ex);
            logger?.Error($"{label}: {failureMessage}");
            stepResult.Message = AppendDuration(failureMessage, stopwatch);
            stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
            state.Commit(stepIndex, stepResult);
            return new PipelineStepOutcome { Failed = true, FailureProfileOutputIsSafe = failureProfileOutputIsSafe };
        }

        stepResult.Message = AppendDuration(stepResult.Message, stopwatch);
        stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
        if (cacheable && stepResult.Success && !string.IsNullOrWhiteSpace(stepFingerprint))
        {
            string currentLastBuildOutPath;
            lock (state.Gate)
                currentLastBuildOutPath = state.LastBuildOutPath;
            var outputs = task.Equals("sitemap", StringComparison.OrdinalIgnoreCase)
                ? GetExpectedStepOutputs(task, step, baseDir, currentLastBuildOutPath) : expectedOutputs;
            lock (state.Gate)
            {
                cacheStateLocal!.Entries[cacheKey] = new WebPipelineCacheEntry
                {
                    Fingerprint = stepFingerprint,
                    Message = stepResult.Message
                };
                state.CacheUpdated = true;
                state.CacheOutputs[cacheKey] = outputs;
            }
        }
        state.Commit(stepIndex, stepResult);
        if (state.ProfileEnabled)
            logger?.Info($"Finished {label} in {FormatDuration(stopwatch.Elapsed)}");
        return CompletedStepOutcome;
    }

    private static void PublishLastBuildState(
        PipelineRunState state,
        string originalOutPath,
        string[] originalUpdatedFiles,
        string stepOutPath,
        string[] stepUpdatedFiles)
    {
        // Only steps that changed the shared build context publish it; concurrent
        // read-only steps can therefore never overwrite each other's view.
        lock (state.Gate)
        {
            if (!string.Equals(originalOutPath, stepOutPath, StringComparison.Ordinal))
                state.LastBuildOutPath = stepOutPath;
            if (!ReferenceEquals(originalUpdatedFiles, stepUpdatedFiles))
                state.LastBuildUpdatedFiles = stepUpdatedFiles;
        }
    }
}
