namespace PowerForge;

/// <summary>
/// Binds a directory pathname to the physical directory that existed when a
/// release operation accepted it.
/// </summary>
internal sealed class AppleStableDirectoryIdentity
{
    private readonly string _description;
    private readonly string _identity;
    private readonly string _requestedPath;

    private AppleStableDirectoryIdentity(
        string requestedPath,
        string physicalPath,
        string description,
        string identity)
    {
        _requestedPath = requestedPath;
        Path = physicalPath;
        _description = description;
        _identity = identity;
    }

    internal string Path { get; }

    internal static AppleStableDirectoryIdentity Capture(
        string path,
        string description)
    {
        var requestedPath = System.IO.Path.GetFullPath(path);
        var physicalPath = AppleReleaseArtifactService.ResolvePhysicalPath(requestedPath);
        var identity = ExistingFilePathIdentityResolver
            .ResolveDirectoryStatus(physicalPath)
            .Identity;
        return new AppleStableDirectoryIdentity(
            requestedPath,
            physicalPath,
            description,
            identity);
    }

    internal void ValidateUnchanged()
    {
        var currentRequestedPhysicalPath = AppleReleaseArtifactService.ResolvePhysicalPath(
            _requestedPath);
        var comparison = FrameworkCompatibility.GetPathStringComparisonForPath(Path);
        if (!Path.Equals(currentRequestedPhysicalPath, comparison))
        {
            throw new InvalidOperationException(
                $"{_description} changed to a different symbolic-link or path alias after validation: {_requestedPath}");
        }

        var currentPhysicalPath = AppleReleaseArtifactService.ResolvePhysicalPath(Path);
        if (!Path.Equals(currentPhysicalPath, comparison))
            throw new InvalidOperationException($"{_description} changed after validation: {Path}");

        var currentIdentity = ExistingFilePathIdentityResolver
            .ResolveDirectoryStatus(currentPhysicalPath)
            .Identity;
        if (!currentIdentity.Equals(_identity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{_description} changed after validation: {Path}");
        }
    }

    /// <summary>
    /// Removes an owned directory only while the accepted path still resolves
    /// to the same physical directory identity.
    /// </summary>
    internal void DeleteOwnedDirectoryIfUnchanged()
    {
        ValidateUnchanged();
        AppleArtifactCopy.DeleteOwnedDirectory(Path);
    }
}
