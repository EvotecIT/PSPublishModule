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
        : this(name, source, kind, trusted: true)
    {
    }

    /// <summary>
    /// Creates a repository descriptor with explicit trust evidence.
    /// </summary>
    /// <param name="name">Friendly repository name.</param>
    /// <param name="source">Repository URL or local folder path.</param>
    /// <param name="kind">Repository kind, or auto to infer from the source.</param>
    /// <param name="trusted">True when the repository is trusted by profile or caller policy.</param>
    public ManagedModuleRepository(
        string name,
        string source,
        ManagedModuleRepositoryKind kind,
        bool trusted)
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
        Trusted = trusted;
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

    /// <summary>
    /// True when repository trust was asserted by an explicit profile or caller policy.
    /// </summary>
    public bool Trusted { get; }

    private static ManagedModuleRepositoryKind InferKind(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
                return ManagedModuleRepositoryKind.LocalFolder;

            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return source.IndexOf("/api/v2", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ManagedModuleRepositoryKind.NuGetV2
                    : ManagedModuleRepositoryKind.NuGetV3;
            }
        }

        if (Path.IsPathRooted(source) || source.StartsWith(".", StringComparison.Ordinal))
            return ManagedModuleRepositoryKind.LocalFolder;

        if (source.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            return ManagedModuleRepositoryKind.LocalFolder;

        return ManagedModuleRepositoryKind.NuGetV3;
    }
}
