using System.Collections;

namespace PowerForge;

/// <summary>
/// Owns an immutable snapshot of an array passed into semantic IR.
/// The wrapper never exposes its retained storage and keeps the familiar array length/index shape.
/// </summary>
internal readonly struct PowerShellImmutableArray<T> : IReadOnlyList<T>
{
    private readonly T[]? _items;

    internal PowerShellImmutableArray(IEnumerable<T>? items)
        => _items = items?.ToArray() ?? Array.Empty<T>();

    internal int Length => _items?.Length ?? 0;
    public int Count => Length;
    public T this[int index] => (_items ?? Array.Empty<T>())[index];

    internal T[] ToArray() => _items?.ToArray() ?? Array.Empty<T>();

    public IEnumerator<T> GetEnumerator()
        => ((_items ?? Array.Empty<T>()) as IEnumerable<T>).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator PowerShellImmutableArray<T>(T[]? items) => new(items);
}
