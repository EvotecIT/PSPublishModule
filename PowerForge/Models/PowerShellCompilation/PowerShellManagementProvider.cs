using System;

namespace PowerForge;

/// <summary>One deterministic command shape read from CDXML metadata.</summary>
public sealed class PowerShellCdxmlCommand
{
    /// <summary>PowerShell command name.</summary>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>Underlying management method or query role.</summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>Declared parameters ordered by metadata identity.</summary>
    public string[] Parameters { get; set; } = Array.Empty<string>();
}

/// <summary>Deterministic CDXML metadata parsed without module import or management-target access.</summary>
public sealed class PowerShellCdxmlMetadata
{
    /// <summary>Metadata schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>CDXML schema URI.</summary>
    public string SchemaUri { get; set; } = string.Empty;

    /// <summary>Management class name.</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>Class version.</summary>
    public string ClassVersion { get; set; } = string.Empty;

    /// <summary>Default PowerShell noun.</summary>
    public string DefaultNoun { get; set; } = string.Empty;

    /// <summary>Declared commands.</summary>
    public PowerShellCdxmlCommand[] Commands { get; set; } = Array.Empty<PowerShellCdxmlCommand>();

    /// <summary>SHA-256 of the exact CDXML input.</summary>
    public string SourceSha256 { get; set; } = string.Empty;
}
