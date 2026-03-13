namespace PowerForge;

/// <summary>
/// Request to execute <c>dotnet nuget sign</c>.
/// </summary>
public sealed class DotNetNuGetSignRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetNuGetSignRequest"/> class.
    /// </summary>
    /// <param name="packagePath">Package path to sign.</param>
    /// <param name="certificateFingerprint">SHA256 certificate fingerprint.</param>
    /// <param name="certificateStoreLocation">Certificate store location.</param>
    /// <param name="timeStampServer">Timestamp server URL.</param>
    /// <param name="certificateStoreName">Certificate store name. Defaults to <c>My</c>.</param>
    /// <param name="overwrite">When true, passes <c>--overwrite</c>.</param>
    /// <param name="workingDirectory">Optional working directory override.</param>
    /// <param name="timeout">Optional timeout override.</param>
    public DotNetNuGetSignRequest(
        string packagePath,
        string certificateFingerprint,
        string certificateStoreLocation,
        string timeStampServer,
        string certificateStoreName = "My",
        bool overwrite = true,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        PackagePath = packagePath;
        CertificateFingerprint = certificateFingerprint;
        CertificateStoreLocation = certificateStoreLocation;
        CertificateStoreName = certificateStoreName;
        TimeStampServer = timeStampServer;
        Overwrite = overwrite;
        WorkingDirectory = workingDirectory;
        Timeout = timeout;
    }

    /// <summary>
    /// Gets the package path to sign.
    /// </summary>
    public string PackagePath { get; }

    /// <summary>
    /// Gets the SHA256 certificate fingerprint.
    /// </summary>
    public string CertificateFingerprint { get; }

    /// <summary>
    /// Gets the certificate store location.
    /// </summary>
    public string CertificateStoreLocation { get; }

    /// <summary>
    /// Gets the certificate store name.
    /// </summary>
    public string CertificateStoreName { get; }

    /// <summary>
    /// Gets the timestamp server URL.
    /// </summary>
    public string TimeStampServer { get; }

    /// <summary>
    /// Gets a value indicating whether <c>--overwrite</c> should be passed.
    /// </summary>
    public bool Overwrite { get; }

    /// <summary>
    /// Gets the optional working directory override.
    /// </summary>
    public string? WorkingDirectory { get; }

    /// <summary>
    /// Gets the optional timeout override.
    /// </summary>
    public TimeSpan? Timeout { get; }
}
