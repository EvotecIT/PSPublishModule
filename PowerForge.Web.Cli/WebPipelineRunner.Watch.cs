using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    internal static int WatchPipeline(
        string pipelinePath,
        WebConsoleLogger logger,
        bool forceProfile = false,
        bool fast = false,
        string? mode = null,
        string[]? onlyTasks = null,
        string[]? skipTasks = null)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        logger.Info("Watch mode enabled (Ctrl+C to stop).");
        var exitCode = 0;
        WatchPipelineLoop(
            pipelinePath,
            logger,
            cts.Token,
            forceProfile,
            fast,
            mode,
            onlyTasks,
            skipTasks,
            onRunCompleted: res => exitCode = res.Success ? 0 : 1);
        return exitCode;
    }

    private static void WatchPipelineLoop(
        string pipelinePath,
        WebConsoleLogger logger,
        CancellationToken token,
        bool forceProfile,
        bool fast,
        string? mode,
        string[]? onlyTasks,
        string[]? skipTasks,
        Action<WebPipelineResult>? onRunCompleted = null)
    {
        var normalizedPipelinePath = Path.GetFullPath(pipelinePath.Trim().Trim('"'));
        var baseDir = Path.GetDirectoryName(normalizedPipelinePath) ?? ".";
        var ignoreRoots = CollectWatchIgnoreRoots(pipelinePath, baseDir);
        var ignoreRootStrings = ignoreRoots.Select(p => p.Replace('\\', '/')).ToArray();

        var gate = new object();
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastEventUtc = DateTime.UtcNow;
        using var signal = new ManualResetEventSlim(false);

        void OnFsEvent(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var fullPath = SafeGetFullPath(path);
            if (string.IsNullOrWhiteSpace(fullPath))
                return;

            if (IsUnderAnyRoot(fullPath, ignoreRoots))
                return;

            // Drop noisy temp/editor artifacts.
            var name = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (name.StartsWith("~", StringComparison.Ordinal) ||
                name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".swp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ".DS_Store", StringComparison.OrdinalIgnoreCase))
                return;

            lock (gate)
            {
                pending.Add(fullPath);
                lastEventUtc = DateTime.UtcNow;
                signal.Set();
            }
        }

        using var watcher = new FileSystemWatcher(baseDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        watcher.Changed += (_, e) => OnFsEvent(e.FullPath);
        watcher.Created += (_, e) => OnFsEvent(e.FullPath);
        watcher.Deleted += (_, e) => OnFsEvent(e.FullPath);
        watcher.Renamed += (_, e) =>
        {
            OnFsEvent(e.OldFullPath);
            OnFsEvent(e.FullPath);
        };
        watcher.Error += (_, e) =>
        {
            // FileSystemWatcher can overflow its internal buffer on large change bursts.
            // In that case, we still trigger a rebuild to recover.
            logger.Warn($"watcher: {e.GetException()?.Message ?? "unknown error"}");
            lock (gate)
            {
                lastEventUtc = DateTime.UtcNow;
                signal.Set();
            }
        };
        watcher.EnableRaisingEvents = true;

        // Initial run.
        RunOnce();

        while (!token.IsCancellationRequested)
        {
            try
            {
                signal.Wait(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Debounce: wait until the file system quiets down a bit.
            while (!token.IsCancellationRequested)
            {
                DateTime last;
                lock (gate) last = lastEventUtc;
                if (DateTime.UtcNow - last >= DefaultWatchDebounce)
                    break;

                try
                {
                    Task.Delay(50, token).Wait(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (token.IsCancellationRequested)
                break;

            string[] changed;
            lock (gate)
            {
                changed = pending.Take(15).ToArray();
                pending.Clear();
                signal.Reset();
            }

            var changedPreview = changed.Length == 0 ? "changes detected" : string.Join(", ", changed.Select(Path.GetFileName).Distinct().Take(5));
            logger.Info($"watch: {changedPreview} -> rebuilding...");
            RunOnce();
        }

        logger.Info("Watch stopped.");
        return;

        void RunOnce()
        {
            var result = RunPipeline(
                pipelinePath,
                logger,
                forceProfile: forceProfile,
                fast: fast,
                mode: mode,
                onlyTasks: onlyTasks,
                skipTasks: skipTasks);

            foreach (var step in result.Steps)
            {
                if (step.Success)
                    logger.Success($"{step.Task}: {step.Message}");
                else
                    logger.Error($"{step.Task}: {step.Message}");
            }

            logger.Info($"Pipeline duration: {result.DurationMs} ms");
            if (!string.IsNullOrWhiteSpace(result.CachePath))
                logger.Info($"Pipeline cache: {result.CachePath}");
            if (!string.IsNullOrWhiteSpace(result.ProfilePath))
                logger.Info($"Pipeline profile: {result.ProfilePath}");

            onRunCompleted?.Invoke(result);
        }
    }

    private static string? SafeGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUnderAnyRoot(string path, IReadOnlyList<string> roots)
    {
        if (roots.Count == 0) return false;

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (path.StartsWith(root, FileSystemPathComparison))
                return true;
        }
        return false;
    }

    private static List<string> CollectWatchIgnoreRoots(string pipelinePath, string baseDir)
    {
        var ignore = new List<string>();

        void AddRoot(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var full = SafeGetFullPath(value);
            if (string.IsNullOrWhiteSpace(full))
                return;
            if (full.StartsWith(SafeGetFullPath(baseDir) ?? baseDir, FileSystemPathComparison) == false)
                return;
            if (!full.EndsWith(Path.DirectorySeparatorChar))
                full += Path.DirectorySeparatorChar;
            ignore.Add(full);
        }

        // Common noisy roots.
        AddRoot(Path.Combine(baseDir, ".git"));
        AddRoot(Path.Combine(baseDir, ".powerforge"));
        AddRoot(Path.Combine(baseDir, "bin"));
        AddRoot(Path.Combine(baseDir, "obj"));

        // Output roots inferred from pipeline steps (best-effort).
        try
        {
            using var doc = LoadPipelineDocumentWithExtends(pipelinePath);
            var root = doc.RootElement;
            if (root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in steps.EnumerateArray())
                {
                    foreach (var key in new[] { "out", "output", "siteRoot", "site-root", "destination", "dest", "htmlOutput", "htmlOut" })
                    {
                        var value = GetString(step, key);
                        if (string.IsNullOrWhiteSpace(value))
                            continue;
                        var resolved = ResolvePath(baseDir, value);
                        if (string.IsNullOrWhiteSpace(resolved))
                            continue;
                        AddRoot(resolved);
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return ignore
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(v => v.Length) // more specific first
            .ToList();
    }

    private static string? GetSkipReason(
        string task,
        JsonElement step,
        string effectiveMode,
        HashSet<string>? onlyTasks,
        HashSet<string>? skipTasks)
    {
        if (!ShouldExecuteTask(task, onlyTasks, skipTasks))
            return skipTasks is not null && skipTasks.Contains(task) ? "skip" : "only";

        if (!ShouldExecuteStepMode(step, effectiveMode))
            return $"mode={effectiveMode}";

        return null;
    }

    private static bool ShouldExecuteTask(string task, HashSet<string>? onlyTasks, HashSet<string>? skipTasks)
    {
        if (skipTasks is not null && skipTasks.Contains(task))
            return false;
        if (onlyTasks is null || onlyTasks.Count == 0)
            return true;
        return onlyTasks.Contains(task);
    }

    private static bool ShouldExecuteStepMode(JsonElement step, string effectiveMode)
    {
        // Absent mode constraints => step runs in all modes.
        var mode = GetString(step, "mode");
        var modes = GetArrayOfStrings(step, "modes") ??
                    GetArrayOfStrings(step, "onlyModes") ?? GetArrayOfStrings(step, "only-modes");
        if (!string.IsNullOrWhiteSpace(mode))
            modes = modes is null ? new[] { mode } : modes.Concat(new[] { mode }).ToArray();

        var skipModes = GetArrayOfStrings(step, "skipModes") ?? GetArrayOfStrings(step, "skip-modes");
        if (skipModes is not null &&
            skipModes.Any(m => string.Equals(m?.Trim(), effectiveMode, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (modes is null || modes.Length == 0)
            return true;

        return modes.Any(m => string.Equals(m?.Trim(), effectiveMode, StringComparison.OrdinalIgnoreCase));
    }

    private static List<PipelineStepDefinition> BuildStepDefinitions(JsonElement stepsElement)
    {
        var steps = new List<PipelineStepDefinition>();
        var aliases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var step in stepsElement.EnumerateArray())
        {
            var task = GetString(step, "task")?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(task))
                continue;

            index++;
            var id = GetString(step, "id");
            if (string.IsNullOrWhiteSpace(id))
                id = $"{task}-{index}";

            if (aliases.ContainsKey(id))
                throw new InvalidOperationException($"Duplicate pipeline step id '{id}'.");

            aliases[id] = index;
            aliases[$"{task}#{index}"] = index;
            if (!aliases.ContainsKey(task))
                aliases[task] = index;

            steps.Add(new PipelineStepDefinition
            {
                Index = index,
                Task = task,
                Id = id,
                DependsOn = ParseDependsOn(step),
                Element = step
            });
        }

        foreach (var step in steps)
        {
            if (step.DependsOn.Length == 0)
                continue;

            var resolved = new List<int>();
            foreach (var dependency in step.DependsOn)
            {
                if (string.IsNullOrWhiteSpace(dependency))
                    continue;

                if (int.TryParse(dependency, out var numeric))
                {
                    if (numeric <= 0 || numeric > steps.Count)
                        throw new InvalidOperationException($"Step '{step.Id}' has invalid dependsOn reference '{dependency}'.");
                    resolved.Add(numeric);
                    continue;
                }

                if (!aliases.TryGetValue(dependency, out var dependencyIndex))
                    throw new InvalidOperationException($"Step '{step.Id}' has unknown dependsOn reference '{dependency}'.");

                resolved.Add(dependencyIndex);
            }

            step.DependencyIndexes = resolved
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

            if (step.DependencyIndexes.Any(value => value >= step.Index))
                throw new InvalidOperationException($"Step '{step.Id}' has dependsOn reference to current/future step.");
        }

        return steps;
    }

    private static string[] ParseDependsOn(JsonElement step)
    {
        var array = GetArrayOfStrings(step, "dependsOn") ?? GetArrayOfStrings(step, "depends-on");
        if (array is { Length: > 0 })
            return array;

        var value = GetString(step, "dependsOn") ?? GetString(step, "depends-on");
        return CliPatternHelper.SplitPatterns(value);
    }

    private static string AppendDuration(string? message, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        var duration = FormatDuration(stopwatch.Elapsed);
        var baseMessage = string.IsNullOrWhiteSpace(message) ? "Completed" : message;
        return $"{baseMessage} ({duration})";
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
            return $"{elapsed.TotalMilliseconds:0} ms";
        if (elapsed.TotalMinutes < 1)
            return $"{elapsed.TotalSeconds:0.0} s";
        return $"{elapsed.TotalMinutes:0.0} min";
    }

    private static string BuildOptimizeSummary(WebOptimizeResult result)
    {
        var parts = new List<string> { $"updated {result.UpdatedCount}" };

        if (result.HtmlSelectedFileCount > 0 && result.HtmlFileCount > 0 && result.HtmlSelectedFileCount != result.HtmlFileCount)
            parts.Add($"html-scope {result.HtmlSelectedFileCount}/{result.HtmlFileCount}");

        if (result.CriticalCssInlinedCount > 0)
            parts.Add($"critical-css {result.CriticalCssInlinedCount}");
        if (result.HtmlMinifiedCount > 0)
            parts.Add($"html {result.HtmlMinifiedCount}");
        if (result.CssMinifiedCount > 0)
            parts.Add($"css {result.CssMinifiedCount}");
        if (result.JsMinifiedCount > 0)
            parts.Add($"js {result.JsMinifiedCount}");
        if (result.HtmlBytesSaved > 0)
            parts.Add($"html-saved {result.HtmlBytesSaved}B");
        if (result.CssBytesSaved > 0)
            parts.Add($"css-saved {result.CssBytesSaved}B");
        if (result.JsBytesSaved > 0)
            parts.Add($"js-saved {result.JsBytesSaved}B");
        if (result.ImageOptimizedCount > 0)
            parts.Add($"images {result.ImageOptimizedCount}");
        if (result.ImageBytesSaved > 0)
            parts.Add($"images-saved {result.ImageBytesSaved}B");
        if (result.ImageFailedCount > 0)
            parts.Add($"image-fails {result.ImageFailedCount}");
        if (result.ImageVariantCount > 0)
            parts.Add($"image-variants {result.ImageVariantCount}");
        if (result.ImageHtmlRewriteCount > 0)
            parts.Add($"image-rewrites {result.ImageHtmlRewriteCount}");
        if (result.ImageHintedCount > 0)
            parts.Add($"image-hints {result.ImageHintedCount}");
        if (result.OptimizedImages.Length > 0)
        {
            var top = result.OptimizedImages[0];
            parts.Add($"top-image {top.Path}(-{top.BytesSaved}B)");
        }
        if (result.ImageFailures.Length > 0)
        {
            var top = result.ImageFailures[0];
            var err = TruncateForLog(top.Error, 90);
            parts.Add($"top-fail {top.Path}({err})");
        }
        if (result.ImageBudgetExceeded)
            parts.Add("image-budget-exceeded");

        if (result.HashedAssetCount > 0)
            parts.Add($"hashed {result.HashedAssetCount}");
        if (result.CacheHeadersWritten)
            parts.Add("headers");
        if (!string.IsNullOrWhiteSpace(result.ReportPath))
            parts.Add($"report {ShortenReportPath(result.ReportPath)}");

        return $"Optimize {string.Join(", ", parts)}";
    }

    private static string ShortenReportPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var normalized = path.Replace('\\', '/');
        var marker = "/.powerforge/";
        var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return normalized.Substring(idx + 1); // strip leading slash

        return Path.GetFileName(normalized);
    }
}
