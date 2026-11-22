using System.Diagnostics;

namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
#if !NET472
    private DependencyOverlap? TryStartDependencyOverlap(
        ManagedModuleInstallRequest request,
        ManagedModulePackageMetadata? metadata,
        string cacheDirectory,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        if (context.IsDependencyInstall || cancellationToken.IsCancellationRequested ||
            request.RestoreModuleDirectoryVersion is not null ||
            request.DependencyConcurrency == 1 || Environment.ProcessorCount == 1 ||
            !ShouldUseDependencyPackagePrefetch(request))
            return null;

        // Limit speculative staging to one pinned direct dependency. The existing branch
        // scheduler owns larger and nested graphs.
        var dependencies = SelectDependencies(metadata).Take(2).ToArray();
        if (dependencies.Length != 1)
            return null;
        var dependency = dependencies[0];
        var range = ManagedModuleVersionRange.Parse(dependency.VersionRange);
        if (range.ExactVersion is null || HasSatisfiedDependency(request, dependency.Id, range, context))
            return null;

        return new DependencyOverlap(
            (parentValidation, token) => InstallDependencyBranchAsync(
                request, dependency, cacheDirectory, context.CreateBranch(parentValidation), token),
            cancellationToken);
    }

    private sealed class DependencyOverlap : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly TaskCompletionSource<bool> _parentValidation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly Task<ManagedModuleInstallResult> _operation;

        internal DependencyOverlap(
            Func<Task, CancellationToken, Task<ManagedModuleInstallResult>> install,
            CancellationToken cancellationToken)
        {
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _operation = Task.Run(() => install(_parentValidation.Task, _cancellation.Token));
        }

        internal TimeSpan Elapsed => _elapsed.Elapsed;

        internal async Task<IReadOnlyList<ManagedModuleInstallResult>> CompleteAsync()
        {
            _parentValidation.TrySetResult(true);
            var dependency = await _operation.ConfigureAwait(false);
            _elapsed.Stop();
            return new[] { dependency };
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_parentValidation.TrySetCanceled())
                    await _cancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (AggregateException)
            {
                // A cancellation callback must not replace the parent failure.
            }
            finally
            {
                try
                {
                    // Observe the background error without replacing the root's validation
                    // error. CompleteAsync propagates dependency errors after validation.
                    await _operation.ConfigureAwait(false);
                }
                catch
                {
                }
                _cancellation.Dispose();
            }
        }
    }
#endif
}
