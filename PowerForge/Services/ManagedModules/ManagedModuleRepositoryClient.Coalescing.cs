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
        Func<Task<IReadOnlyList<ManagedModuleVersionInfo>>> factory)
    {
        var key = BuildCoalescedQueryKey("versions", repository, packageId, includePrerelease, take: null, credential, cancellationToken);
        return key is null
            ? factory()
            : ExecuteCoalescedAsync(_versionQueryTasks, key, factory);
    }

    private Task<ManagedModuleVersionInfo?> ExecuteCoalescedLatestVersionQueryAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken,
        Func<Task<ManagedModuleVersionInfo?>> factory)
    {
        var key = BuildCoalescedQueryKey("latest", repository, packageId, includePrerelease, take: null, credential, cancellationToken);
        return key is null
            ? factory()
            : ExecuteCoalescedAsync(_latestVersionQueryTasks, key, factory);
    }

    private Task<IReadOnlyList<ManagedModuleVersionInfo>> ExecuteCoalescedSearchQueryAsync(
        ManagedModuleRepository repository,
        string query,
        bool includePrerelease,
        int take,
        RepositoryCredential? credential,
        CancellationToken cancellationToken,
        Func<Task<IReadOnlyList<ManagedModuleVersionInfo>>> factory)
    {
        var key = BuildCoalescedQueryKey("search", repository, query, includePrerelease, take, credential, cancellationToken);
        return key is null
            ? factory()
            : ExecuteCoalescedAsync(_searchQueryTasks, key, factory);
    }

    private static async Task<T> ExecuteCoalescedAsync<T>(
        ConcurrentDictionary<string, Lazy<Task<T>>> tasks,
        string key,
        Func<Task<T>> factory)
    {
        var lazy = tasks.GetOrAdd(
            key,
            _ => new Lazy<Task<T>>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<T>>>>)tasks)
                .Remove(new KeyValuePair<string, Lazy<Task<T>>>(key, lazy));
        }
    }

    private static string? BuildCoalescedQueryKey(
        string operation,
        ManagedModuleRepository repository,
        string value,
        bool includePrerelease,
        int? take,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (credential is not null || cancellationToken.CanBeCanceled)
            return null;

        return string.Join(
            "|",
            operation,
            repository.Kind.ToString(),
            NormalizeCoalescedRepositorySource(repository.Source),
            value.Trim(),
            includePrerelease ? "pre" : "stable",
            take?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static string NormalizeCoalescedRepositorySource(string source)
    {
        var trimmed = source.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return NormalizeRepositorySourceCacheKey(trimmed.TrimEnd('/', '\\'));
    }
}
