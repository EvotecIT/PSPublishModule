namespace PowerForge;

/// <summary>
/// Retains a verified deployment product in content-addressed DerivedData so
/// public deployment results never advertise a private path that cleanup removes.
/// </summary>
internal static class AppleBuiltAppResultStore
{
    internal static string Preserve(
        AppleBuiltAppSnapshot snapshot,
        AppleStableDirectoryIdentity derivedDataDirectory)
    {
        if (derivedDataDirectory is null)
            throw new ArgumentNullException(nameof(derivedDataDirectory));

        derivedDataDirectory.ValidateUnchanged();
        snapshot.ValidateUnchanged();
        var identity = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
            snapshot.AppPath,
            "private built Apple app snapshot");
        var outputRoot = Path.Combine(
            derivedDataDirectory.Path,
            "PowerForge",
            "DeploymentProducts");
        var bundleName = Path.GetFileName(snapshot.AppPath);
        var contentRoot = Path.Combine(outputRoot, identity.Sha256);
        var retainedRoot = Path.Combine(
            contentRoot,
            ComputeBundleNameKey(bundleName));
        var retainedAppPath = Path.Combine(
            retainedRoot,
            bundleName);

        CreateDirectoryWithoutLinkedAncestors(
            outputRoot,
            "Apple deployment result store");
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                outputRoot,
                File.GetUnixFileMode(outputRoot) |
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
#endif
        CreateDirectoryWithoutLinkedAncestors(
            contentRoot,
            "Apple deployment result content store");
        if (Directory.Exists(retainedRoot) || File.Exists(retainedRoot))
        {
            ValidateRetainedProduct(retainedAppPath, identity.Sha256);
            snapshot.ValidateUnchanged();
            return retainedAppPath;
        }

        var stageRoot = Path.Combine(
            outputRoot,
            $".powerforge-stage-{Guid.NewGuid():N}");
        var stagedAppPath = Path.Combine(
            stageRoot,
            bundleName);
        CreateDirectoryWithoutLinkedAncestors(
            stageRoot,
            "Apple deployment result staging directory");
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                stageRoot,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
#endif
        try
        {
            AppleArtifactCopy.CopyDirectory(snapshot.AppPath, stagedAppPath);
            ValidateRetainedProduct(stagedAppPath, identity.Sha256);
            snapshot.ValidateUnchanged();
            try
            {
                EnsurePathHasNoLinkedAncestor(
                    retainedRoot,
                    "retained Apple deployment product root");
                Directory.Move(stageRoot, retainedRoot);
            }
            catch (IOException) when (Directory.Exists(retainedRoot))
            {
                ValidateRetainedProduct(retainedAppPath, identity.Sha256);
                return retainedAppPath;
            }

            ValidateRetainedProduct(retainedAppPath, identity.Sha256);
            return retainedAppPath;
        }
        finally
        {
            if (Directory.Exists(stageRoot))
            {
                try { AppleArtifactCopy.DeleteOwnedDirectory(stageRoot); } catch { /* best effort private cleanup */ }
            }
        }
    }

    private static string ComputeBundleNameKey(string bundleName)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(
                sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(bundleName)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static void ValidateRetainedProduct(
        string appPath,
        string expectedSha256)
    {
        EnsurePathHasNoLinkedAncestor(
            appPath,
            "retained Apple deployment product");
        var regularIdentity = AppleArtifactCopy.CaptureRegularPathIdentity(
            appPath,
            "retained Apple deployment product",
            requireDirectory: true);
        if (regularIdentity is null ||
            !regularIdentity.Sha256.Equals(
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The retained Apple deployment product does not match the provenance-bound build. " +
                "Remove the affected DerivedData and rebuild the app.");
        }
    }

    private static void EnsurePathHasNoLinkedAncestor(
        string path,
        string description)
    {
        var fullPath = Path.GetFullPath(path);
        var physicalPath = AppleReleaseArtifactService.ResolvePhysicalPath(fullPath);
        if (!fullPath.Equals(
                physicalPath,
                FrameworkCompatibility.GetPathStringComparisonForPath(fullPath)))
        {
            throw new InvalidOperationException(
                $"The {description} must not traverse a symbolic link or reparse point: {fullPath}");
        }
    }

    private static void CreateDirectoryWithoutLinkedAncestors(
        string path,
        string description)
    {
        EnsurePathHasNoLinkedAncestor(path, description);
        Directory.CreateDirectory(path);
        EnsurePathHasNoLinkedAncestor(path, description);
    }
}
