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
