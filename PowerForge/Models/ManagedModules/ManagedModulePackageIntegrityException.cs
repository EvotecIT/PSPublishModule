namespace PowerForge;

/// <summary>
/// Exception thrown when a managed module package fails caller-supplied integrity requirements.
/// </summary>
public sealed class ManagedModulePackageIntegrityException : InvalidOperationException
{
    /// <summary>
    /// Creates a package integrity exception.
    /// </summary>
    /// <param name="moduleName">Module or package id being delivered.</param>
    /// <param name="version">Package version being delivered.</param>
    /// <param name="expectedSha256">Expected SHA256 hash supplied by the caller.</param>
    /// <param name="actualSha256">Actual SHA256 hash computed from the package.</param>
    /// <param name="packagePath">Package path that was verified.</param>
    public ManagedModulePackageIntegrityException(
        string moduleName,
        string version,
        string expectedSha256,
        string actualSha256,
        string packagePath)
        : base($"Package '{moduleName}' {version} failed SHA256 integrity verification. Expected {expectedSha256}, actual {actualSha256}.")
    {
        ModuleName = moduleName;
        Version = version;
        ExpectedSha256 = expectedSha256;
        ActualSha256 = actualSha256;
        PackagePath = packagePath;
    }

    /// <summary>
    /// Module or package id being delivered.
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// Package version being delivered.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Expected SHA256 hash supplied by the caller.
    /// </summary>
    public string ExpectedSha256 { get; }

    /// <summary>
    /// Actual SHA256 hash computed from the package.
    /// </summary>
    public string ActualSha256 { get; }

    /// <summary>
    /// Package path that was verified.
    /// </summary>
    public string PackagePath { get; }
}
