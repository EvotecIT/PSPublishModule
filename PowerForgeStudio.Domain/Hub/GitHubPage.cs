using System.Collections;

namespace PowerForgeStudio.Domain.Hub;

/// <summary>A bounded API listing with explicit coverage, including filtered issue listings.</summary>
public sealed record GitHubPage<T>(IReadOnlyList<T> Items, bool HasMore) : IReadOnlyList<T>
{
    public int Count => Items.Count;
    public T this[int index] => Items[index];
    public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
