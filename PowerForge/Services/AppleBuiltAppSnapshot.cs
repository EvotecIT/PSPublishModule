namespace PowerForge;

/// <summary>
/// Copies one xcodebuild product into a private deployment input and revalidates
/// its complete content and physical identity after the local installer reads it.
/// </summary>
internal sealed class AppleBuiltAppSnapshot : IDisposable
{
    private readonly AppleArchiveUploadSnapshot _snapshot;
    private readonly string _expectedSha256;
    private bool _disposed;

    private AppleBuiltAppSnapshot(
        AppleArchiveUploadSnapshot snapshot,
        string expectedSha256)
    {
        _snapshot = snapshot;
        _expectedSha256 = expectedSha256;
    }

    internal string AppPath => _snapshot.ArchivePath;

    internal static AppleBuiltAppSnapshot Create(string appPath)
    {
        var productPath = Path.GetFullPath(appPath);
        if (!Directory.Exists(productPath))
        {
            throw new DirectoryNotFoundException(
                $"xcodebuild completed but the built app product was not found: {productPath}");
        }

        var sourceIdentity = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
            productPath,
            "xcodebuild app product");
        var expectedSha256 = sourceIdentity.Sha256;
        AppleArchiveUploadSnapshot? snapshot = null;
        try
        {
            // The process-completion callback privately snapshots the exact
            // product before any deploy consumer is allowed to read it. A
            // concurrent source mutation either changes the copied bytes and
            // fails the expected hash or is irrelevant because installation
            // consumes only this private copy.
            snapshot = AppleArchiveUploadSnapshot.Create(productPath, expectedSha256);
            var sourceAfterCopy = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
                productPath,
                "xcodebuild app product");
            if (!sourceIdentity.Equals(sourceAfterCopy))
            {
                throw new InvalidOperationException(
                    "The xcodebuild app product changed while PowerForge was creating its private deployment snapshot. " +
                    "Discard the deployment and rebuild the app.");
            }
            return new AppleBuiltAppSnapshot(snapshot, expectedSha256);
        }
        catch
        {
            snapshot?.Dispose();
            throw;
        }
    }

    internal void ValidateUnchanged()
    {
        try
        {
            _snapshot.ValidateUnchanged(_expectedSha256);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "The private built Apple app snapshot changed while the local installer was consuming it. " +
                "Discard the deployment and rebuild the app.",
                exception);
        }
    }

    internal AppleBuiltAppCopySnapshot CaptureVerifiedCopy(
        string copiedAppPath,
        string description)
    {
        var identity = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
            copiedAppPath,
            description);
        if (!identity.Sha256.Equals(_expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The {description} does not match the provenance-bound built app. " +
                "Discard the deployment and rebuild the app.");
        }
        return new AppleBuiltAppCopySnapshot(
            copiedAppPath,
            description,
            identity);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _snapshot.Dispose();
    }
}

/// <summary>
/// Binds an installer-owned app copy to the exact content and physical identity
/// captured after the copy completed.
/// </summary>
internal sealed class AppleBuiltAppCopySnapshot
{
    private readonly string _appPath;
    private readonly string _description;
    private readonly AppleArchiveUploadSnapshot.SnapshotIdentity _identity;

    internal AppleBuiltAppCopySnapshot(
        string appPath,
        string description,
        AppleArchiveUploadSnapshot.SnapshotIdentity identity)
    {
        _appPath = Path.GetFullPath(appPath);
        _description = description;
        _identity = identity;
    }

    internal void ValidateUnchanged()
    {
        var current = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
            _appPath,
            _description);
        if (!_identity.Equals(current))
        {
            throw new InvalidOperationException(
                $"The {_description} changed before atomic installation. " +
                "The existing app was preserved; discard the deployment and rebuild the app.");
        }
    }
}

/// <summary>
/// Carries a build result together with its optional product-integrity lease.
/// </summary>
internal sealed class AppleAppBuildOperation : IDisposable
{
    private readonly string? _ownedBuildOutputRoot;

    internal AppleAppBuildOperation(
        AppleAppBuildResult result,
        AppleBuiltAppSnapshot? productSnapshot,
        string? ownedBuildOutputRoot = null)
    {
        Result = result;
        ProductSnapshot = productSnapshot;
        _ownedBuildOutputRoot = ownedBuildOutputRoot;
    }

    internal AppleAppBuildResult Result { get; }

    internal AppleBuiltAppSnapshot? ProductSnapshot { get; }

    public void Dispose()
    {
        ProductSnapshot?.Dispose();
        if (!string.IsNullOrWhiteSpace(_ownedBuildOutputRoot))
        {
            try { AppleArtifactCopy.DeleteOwnedDirectory(_ownedBuildOutputRoot!); } catch { /* best effort private cleanup */ }
        }
    }
}
