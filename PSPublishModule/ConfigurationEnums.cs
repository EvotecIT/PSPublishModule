namespace PSPublishModule;

/// <summary>
/// Certificate store location for code-signing operations.
/// </summary>
public enum CertificateStoreLocation
{
    /// <summary>CurrentUser certificate store.</summary>
    CurrentUser,
    /// <summary>LocalMachine certificate store.</summary>
    LocalMachine
}

/// <summary>
/// Which portion of the certificate chain to include in Authenticode signatures.
/// </summary>
public enum CertificateChainInclude
{
    /// <summary>Include the full chain.</summary>
    All,
    /// <summary>Include the chain but exclude the root.</summary>
    NotRoot,
    /// <summary>Include only the signer certificate.</summary>
    Signer
}

/// <summary>
/// Hash algorithm used for Authenticode signatures.
/// </summary>
public enum CertificateHashAlgorithm
{
    /// <summary>SHA1.</summary>
    SHA1,
    /// <summary>SHA256.</summary>
    SHA256,
    /// <summary>SHA384.</summary>
    SHA384,
    /// <summary>SHA512.</summary>
    SHA512
}

/// <summary>
/// Cleanup type used by <c>Remove-ProjectFiles</c>.
/// </summary>
public enum ProjectCleanupType
{
    /// <summary>Build artefacts (bin/obj/etc).</summary>
    Build,
    /// <summary>Log and trace files/folders.</summary>
    Logs,
    /// <summary>HTML files.</summary>
    Html,
    /// <summary>Temporary files/folders.</summary>
    Temp,
    /// <summary>All supported cleanup types combined.</summary>
    All
}

/// <summary>
/// Deletion method used by <c>Remove-ProjectFiles</c>.
/// </summary>
public enum ProjectDeleteMethod
{
    /// <summary>Use standard file system delete operations.</summary>
    RemoveItem,
    /// <summary>Use <c>System.IO</c> delete operations (useful for some cloud-file edge cases).</summary>
    DotNetDelete,
    /// <summary>Move items to the Recycle Bin (Windows only).</summary>
    RecycleBin
}
