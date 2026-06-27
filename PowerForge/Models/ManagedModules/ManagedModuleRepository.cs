namespace PowerForge;

/// <summary>
/// Describes a repository that can be queried by the managed module engine.
/// </summary>
public sealed class ManagedModuleRepository
{
    /// <summary>
    /// Creates a repository descriptor.
    /// </summary>
    /// <param name="name">Friendly repository name.</param>
    /// <param name="source">Repository URL or local folder path.</param>
    /// <param name="kind">Repository kind, or auto to infer from the source.</param>
    public ManagedModuleRepository(string name, string source, ManagedModuleRepositoryKind kind = ManagedModuleRepositoryKind.Auto)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Repository name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Repository source is required.", nameof(source));

        Name = name.Trim();
        Source = source.Trim().Trim('"');
        Kind = kind == ManagedModuleRepositoryKind.Auto
            ? InferKind(Source)
            : kind;
    }

    /// <summary>
    /// Friendly repository name used in results and plans.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Repository URL or local folder path.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Repository protocol.
    /// </summary>
    public ManagedModuleRepositoryKind Kind { get; }

    private static ManagedModuleRepositoryKind InferKind(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            return ManagedModuleRepositoryKind.LocalFolder;

        if (Path.IsPathRooted(source) || source.StartsWith(".", StringComparison.Ordinal))
            return ManagedModuleRepositoryKind.LocalFolder;

        return ManagedModuleRepositoryKind.NuGetV3;
    }
}
