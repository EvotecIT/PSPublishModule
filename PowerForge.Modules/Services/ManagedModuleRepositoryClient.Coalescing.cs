using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private Task<IReadOnlyList<ManagedModuleVersionInfo>> ExecuteCoalescedVersionQueryAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<IReadOnlyList<ManagedModuleVersionInfo>>> factory)
    {
        var key = BuildCoalescedQueryKey("versions", repository, packageId, includePrerelease, take: null, skip: null, credential, cancellationToken);
        return key is null
            ? factory(cancellationToken)
            : ExecuteCoalescedAsync(_versionQueryTasks, key, cancellationToken, factory);
    }

    private Task<ManagedModuleVersionInfo?> ExecuteCoalescedLatestVersionQueryAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<ManagedModuleVersionInfo?>> factory)
    {
        var key = BuildCoalescedQueryKey("latest", repository, packageId, includePrerelease, take: null, skip: null, credential, cancellationToken);
        return key is null
            ? factory(cancellationToken)
            : ExecuteCoalescedAsync(_latestVersionQueryTasks, key, cancellationToken, factory);
    }

    private Task<IReadOnlyList<ManagedModuleVersionInfo>> ExecuteCoalescedSearchQueryAsync(
        ManagedModuleRepository repository,
        string query,
        bool includePrerelease,
        int take,
        int skip,
        RepositoryCredential? credential,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<IReadOnlyList<ManagedModuleVersionInfo>>> factory)
    {
        var key = BuildCoalescedQueryKey("search", repository, query, includePrerelease, take, skip, credential, cancellationToken);
        return key is null
            ? factory(cancellationToken)
            : ExecuteCoalescedAsync(_searchQueryTasks, key, cancellationToken, factory);
    }

    private static async Task<T> ExecuteCoalescedAsync<T>(
        ConcurrentDictionary<string, Lazy<Task<T>>> tasks,
        string key,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> factory)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lazy = tasks.GetOrAdd(
            key,
            _ => new Lazy<Task<T>>(
                () => factory(CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        cancellationToken.ThrowIfCancellationRequested();
        var sharedTask = lazy.Value;
        _ = sharedTask.ContinueWith(
            _ => ((ICollection<KeyValuePair<string, Lazy<Task<T>>>>)tasks)
                .Remove(new KeyValuePair<string, Lazy<Task<T>>>(key, lazy)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return await WaitForSharedTaskAsync(sharedTask, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> WaitForSharedTaskAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
            return await task.ConfigureAwait(false);

        var canceled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<object?>)state!).TrySetResult(null),
            canceled);
        var completed = await Task.WhenAny(task, canceled.Task).ConfigureAwait(false);
        if (!ReferenceEquals(completed, task))
            throw new OperationCanceledException(cancellationToken);

        return await task.ConfigureAwait(false);
    }

    private static string? BuildCoalescedQueryKey(
        string operation,
        ManagedModuleRepository repository,
        string value,
        bool includePrerelease,
        int? take,
        int? skip,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (credential is not null)
            return null;

        return string.Join(
            "|",
            operation,
            repository.Kind.ToString(),
            NormalizeCoalescedRepositorySource(repository.Source),
            value.Trim(),
            includePrerelease ? "pre" : "stable",
            take?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            skip?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static string NormalizeCoalescedRepositorySource(string source)
    {
        var trimmed = source.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return NormalizeRepositorySourceCacheKey(trimmed.TrimEnd('/', '\\'));
    }
}
