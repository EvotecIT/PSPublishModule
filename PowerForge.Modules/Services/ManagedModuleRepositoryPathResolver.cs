namespace PowerForge;

/// <summary>
/// Normalizes local managed-module repository sources across path and file-URI forms.
/// </summary>
internal static class ManagedModuleRepositoryPathResolver
{
    /// <summary>
    /// Resolves a local repository source to its full filesystem path.
    /// </summary>
    internal static string ResolveLocalFolder(string source)
        => NormalizeSource(source, baseDirectory: null);

    /// <summary>
    /// Normalizes a repository source, resolving relative local folders against an explicit base directory.
    /// </summary>
    internal static string NormalizeSource(string source, string? baseDirectory)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            return Path.GetFullPath(uri.LocalPath);

        var path = source.Trim().Trim('"');
        if (!LooksLikeLocalFolder(path))
            return path;

        if (Path.DirectorySeparatorChar == '\\')
            path = path.Replace('/', '\\');
        if (!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(baseDirectory))
            path = Path.Combine(baseDirectory!, path);
        return Path.GetFullPath(path);
    }

    private static bool LooksLikeLocalFolder(string source)
    {
        if (Path.IsPathRooted(source) || source.StartsWith(".", StringComparison.Ordinal))
            return true;

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return uri.IsFile;

        return source.IndexOfAny(new[] { '/', '\\' }) >= 0;
    }
}
