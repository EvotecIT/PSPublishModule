namespace PowerForge.Web;

/// <summary>Cross-reference configuration for docs/API links.</summary>
public sealed class XrefSpec
{
    /// <summary>When false, xref link resolution and validation are disabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Optional JSON map files with xref id-to-url entries.</summary>
    public string[] MapFiles { get; set; } = Array.Empty<string>();
    /// <summary>When true, unresolved xref links produce warnings.</summary>
    public bool WarnOnMissing { get; set; } = true;
    /// <summary>When true, writes the resolved xref map to _powerforge/xrefmap.json.</summary>
    public bool EmitMap { get; set; }
    /// <summary>Maximum unresolved xref warnings emitted per run.</summary>
    public int MaxWarnings { get; set; } = 25;
}
