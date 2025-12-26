namespace PSPublishModule;

/// <summary>
/// Publish destination type for <c>New-ConfigurationPublish</c>.
/// </summary>
public enum PublishDestination
{
    /// <summary>Publish to a PowerShell repository (PSGallery or named repository).</summary>
    PowerShellGallery,
    /// <summary>Publish artefacts to GitHub Releases.</summary>
    GitHub
}

/// <summary>
/// Dependency kind used by <c>New-ConfigurationModule</c>.
/// </summary>
public enum ModuleDependencyKind
{
    /// <summary>Required module dependency (manifest RequiredModules).</summary>
    RequiredModule,
    /// <summary>External module dependency (PSData.ExternalModuleDependencies).</summary>
    ExternalModule,
    /// <summary>Approved module dependency (selectively copied during merge).</summary>
    ApprovedModule
}

/// <summary>
/// Encoding values used by <c>New-ConfigurationFileConsistency</c>.
/// </summary>
public enum FileConsistencyEncoding
{
    /// <summary>ASCII.</summary>
    ASCII,
    /// <summary>UTF-8 (no BOM).</summary>
    UTF8,
    /// <summary>UTF-8 with BOM.</summary>
    UTF8BOM,
    /// <summary>UTF-16 (Little Endian).</summary>
    Unicode,
    /// <summary>UTF-16 (Big Endian).</summary>
    BigEndianUnicode,
    /// <summary>UTF-7.</summary>
    UTF7,
    /// <summary>UTF-32.</summary>
    UTF32
}

/// <summary>
/// Line ending values used by <c>New-ConfigurationFileConsistency</c>.
/// </summary>
public enum FileConsistencyLineEnding
{
    /// <summary>CRLF (Windows).</summary>
    CRLF,
    /// <summary>LF (Unix).</summary>
    LF
}

/// <summary>
/// Destination locations for delivery bundle items (README/CHANGELOG/LICENSE).
/// </summary>
public enum DeliveryBundleDestination
{
    /// <summary>Place files under Internals.</summary>
    Internals,
    /// <summary>Place files in the module root.</summary>
    Root,
    /// <summary>Place files in both Internals and Root.</summary>
    Both,
    /// <summary>Do not place the files.</summary>
    None
}

/// <summary>
/// Documentation tool used by <c>New-ConfigurationDocumentation</c>.
/// </summary>
public enum DocumentationTool
{
    /// <summary>Use PlatyPS to generate markdown help.</summary>
    PlatyPS,
    /// <summary>Use HelpOut to generate markdown help.</summary>
    HelpOut
}

/// <summary>
/// Artefact type for <c>New-ConfigurationArtefact</c>.
/// </summary>
public enum ArtefactType
{
    /// <summary>Unpacked module artefact.</summary>
    Unpacked,
    /// <summary>Packed module artefact (zip).</summary>
    Packed,
    /// <summary>Script artefact (PS1 without PSD1).</summary>
    Script,
    /// <summary>Packed script artefact (zip containing PS1 without PSD1).</summary>
    ScriptPacked
}

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
