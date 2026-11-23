using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    /// <summary>
    /// Rebuilds and runs one executable after stable content changes. Stops the preceding
    /// process tree before rebuilding, waits for edits after failure, and never runs stale output.
    /// Dependency acquisition and changes to the reviewed closure require explicit lock/restore.
    /// </summary>
    public async Task WatchAsync(
        string projectPath,
        PowerShellCompilationProjectRunOptions? options = null,
        Action<PowerShellCompilationProjectRunEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = CopyRunOptions(options);
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        using var lease = AcquireDevelopmentLease(context);
        var inputs = new DevelopmentInputs(context);
        var observed = inputs.Capture(cancellationToken);
        var settled = Stopwatch.StartNew();
        var pending = true;
        CancellationTokenSource? attemptCancellation = null;
        Task<PowerShellCompilationProjectRunResult>? attempt = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pending && settled.ElapsedMilliseconds >= settings.DebounceMilliseconds)
                {
                    await StopAttemptAsync().ConfigureAwait(false);
                    attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    attempt = RunCoreAsync(context.ProjectPath, settings, progress, attemptCancellation.Token);
                    pending = false;
                }
                await Task.Delay(settings.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
                string current;
                try
                {
                    inputs.RefreshContext();
                    current = inputs.Capture(cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    current = "unreadable:" + exception.Message;
                }
                if (!current.Equals(observed, StringComparison.Ordinal))
                {
                    observed = current;
                    pending = true;
                    settled.Restart();
                    await StopAttemptAsync().ConfigureAwait(false);
                    progress?.Invoke(new PowerShellCompilationProjectRunEvent
                    {
                        State = "changed", Message = "Inputs changed; stopped the preceding attempt and waiting for stable edits."
                    });
                }
                else if (attempt?.IsCompleted == true)
                {
                    await attempt.ConfigureAwait(false);
                    attempt = null;
                    attemptCancellation?.Dispose();
                    attemptCancellation = null;
                }
            }
        }
        finally { await StopAttemptAsync().ConfigureAwait(false); }

        async Task StopAttemptAsync()
        {
            attemptCancellation?.Cancel();
            if (attempt is not null)
            {
                try { await attempt.ConfigureAwait(false); }
                catch (OperationCanceledException) when (attemptCancellation?.IsCancellationRequested == true) { }
            }
            attempt = null;
            attemptCancellation?.Dispose();
            attemptCancellation = null;
        }
    }

    // Poll bytes rather than trusting FileSystemWatcher delivery, timestamps or file sizes.
    // Generated outputs are excluded; lock/environment receipts are explicitly included.
    private sealed class DevelopmentInputs
    {
        private readonly string _projectPath;
        private readonly HashSet<string> _receipts = new(PowerShellCompilationPathSafety.PathComparer);

        internal DevelopmentInputs(PowerShellCompilationProjectManifestService.ProjectContext context)
        {
            _projectPath = context.ProjectPath;
            AddContext(context);
        }

        internal void RefreshContext()
        {
            try { AddContext(PowerShellCompilationProjectManifestService.Open(_projectPath)); }
            catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
            {
                // A partly saved manifest is still fingerprinted and retried after the next edit.
            }
        }

        private void AddContext(PowerShellCompilationProjectManifestService.ProjectContext context)
        {
            _receipts.Add(context.ProjectPath);
            // Observe declared files even when parsing fails before dependency discovery.
            // Successful refreshes discover additions through the canonical resolver/planner.
            _receipts.UnionWith(context.Sources.Where(path => !Directory.Exists(path)));
            if (context.EntryPoint is not null) _receipts.Add(context.EntryPoint);
            foreach (var package in context.Manifest.ProviderPackages) _receipts.Add(context.Resolve(package));
            _receipts.Add(context.Resolve(".powerforge/environment/environment.json"));
            foreach (var artifact in context.Manifest.Artifacts)
            {
                _receipts.Add(context.Resolve(artifact.DependencyLock));
                if (!string.IsNullOrWhiteSpace(artifact.ProviderLock)) _receipts.Add(context.Resolve(artifact.ProviderLock!));
                _receipts.Add(context.Resolve($".powerforge/environment/restore/{artifact.Name}/packages.lock.json"));
            }
            foreach (var artifact in context.Manifest.Artifacts)
            {
                try
                {
                    var input = ResolveInput(context, artifact);
                    _receipts.UnionWith(input.SourceFiles);
                    var dependencies = new PowerShellCompilationDependencyPlanner().Analyze(input, artifact.Target.Mode,
                        context.Manifest.Resources.Mode, context.Manifest.Resources.Include, context.Manifest.Resources.Exclude,
                        context.Resolve(artifact.OutputDirectory), GetGeneratedOutputDirectories(context));
                    _receipts.UnionWith(dependencies.Where(dependency => dependency.SourcePath is not null &&
                            dependency.Selection is not (PowerShellCompilationDependencySelection.Excluded or PowerShellCompilationDependencySelection.Unclassified))
                        .Select(dependency => dependency.SourcePath!));
                }
                catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
                {
                    // Watch must also start with invalid source. Source paths remain observed,
                    // and the build attempt reports the precise parsing/dependency failure.
                }
            }
        }

        internal string Capture(CancellationToken cancellationToken)
        {
            var files = new SortedSet<string>(PowerShellCompilationPathSafety.PathComparer);
            files.UnionWith(_receipts);
            using var sha = SHA256.Create();
            var builder = new StringBuilder();
            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(path, "Watched input traverses a link.");
                builder.Append(path.Length).Append(':').Append(path).Append(':')
                    .Append(File.Exists(path) ? PowerShellCompilationProjectManifestService.ComputeSha256(path) : "missing").Append('\n');
            }
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())).Select(value => value.ToString("x2")));
        }
    }
}
