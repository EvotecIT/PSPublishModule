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
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath;

        return Path.GetFullPath(source.Trim().Trim('"'));
    }
}
