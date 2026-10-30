namespace PowerForge;

/// <summary>
/// Tracks operation-local waits atomically across dependency branches. Each lease owns its
/// edges, so completing one sibling cannot hide another sibling's outstanding wait.
/// </summary>
internal sealed class ManagedModuleInstallWaitGraph
{
    private readonly object _sync = new();
    private readonly Dictionary<ManagedModuleInstallPending, Dictionary<ManagedModuleInstallPending, int>> _edges = new();

    public IDisposable EnterWait(IEnumerable<ManagedModuleInstallPending> owners, ManagedModuleInstallPending? target)
    {
        lock (_sync)
        {
            var sources = target is null || target.Task.IsCompleted
                ? Array.Empty<ManagedModuleInstallPending>()
                : owners.Where(owner => !owner.Task.IsCompleted).OrderBy(owner => owner.Name, StringComparer.OrdinalIgnoreCase).ToArray();

            // Check and publish under the same lock, before the caller can await a target.
            foreach (var source in sources)
            {
                var path = FindPath(target!, source);
                if (path is not null)
                    throw CreateCycleException(path.Select(node => node.Name).Concat(new[] { target!.Name }));
            }

            foreach (var source in sources)
            {
                if (!_edges.TryGetValue(source, out var outgoing))
                    _edges[source] = outgoing = new Dictionary<ManagedModuleInstallPending, int>();
                outgoing.TryGetValue(target!, out var count);
                outgoing[target!] = count + 1;
            }

            return new WaitLease(this, sources, target);
        }
    }

    /// <summary>Formats the directed cycle from its smallest module name, independent of which branch detects it.</summary>
    internal static InvalidOperationException CreateCycleException(IEnumerable<string> path)
    {
        var cycle = path.ToArray();
        var count = cycle.Length - 1;
        var start = 0;
        for (var index = 1; index < count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Compare(cycle[index], cycle[start]) < 0)
                start = index;
        }

        var ordered = Enumerable.Range(0, count).Select(index => cycle[(start + index) % count]).ToArray();
        return new InvalidOperationException("Managed module dependency cycle detected: " +
            string.Join(" -> ", ordered.Concat(new[] { ordered[0] })) + ".");
    }

    // Iterative traversal also bounds the detector's stack for long dependency chains.
    private IReadOnlyList<ManagedModuleInstallPending>? FindPath(ManagedModuleInstallPending start, ManagedModuleInstallPending end)
    {
        var previous = new Dictionary<ManagedModuleInstallPending, ManagedModuleInstallPending?> { [start] = null };
        var queue = new Queue<ManagedModuleInstallPending>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (ReferenceEquals(node, end))
            {
                var path = new List<ManagedModuleInstallPending>();
                for (ManagedModuleInstallPending? item = node; item is not null; item = previous[item])
                    path.Add(item);
                path.Reverse();
                return path;
            }

            if (!_edges.TryGetValue(node, out var outgoing))
                continue;
            foreach (var next in outgoing.Keys.Where(next => !next.Task.IsCompleted).OrderBy(next => next.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (previous.ContainsKey(next))
                    continue;
                previous[next] = node;
                queue.Enqueue(next);
            }
        }

        return null;
    }

    private void RemoveWait(ManagedModuleInstallPending[] sources, ManagedModuleInstallPending? target)
    {
        lock (_sync)
        {
            foreach (var source in sources)
            {
                var outgoing = _edges[source];
                if (--outgoing[target!] == 0)
                    outgoing.Remove(target!);
                if (outgoing.Count == 0)
                    _edges.Remove(source);
            }
        }
    }

    private sealed class WaitLease(ManagedModuleInstallWaitGraph graph, ManagedModuleInstallPending[] sources, ManagedModuleInstallPending? target) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                graph.RemoveWait(sources, target);
        }
    }
}
