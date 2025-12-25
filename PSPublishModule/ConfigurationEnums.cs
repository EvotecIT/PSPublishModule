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
