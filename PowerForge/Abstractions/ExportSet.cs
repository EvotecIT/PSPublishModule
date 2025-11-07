namespace PowerForge;

/// <summary>Represents detected exports for a module manifest.</summary>
public sealed class ExportSet
{
    /// <summary>Function names to export.</summary>
    public string[] Functions { get; }
    /// <summary>Cmdlet names to export.</summary>
    public string[] Cmdlets { get; }
    /// <summary>Alias names to export.</summary>
    public string[] Aliases { get; }
    /// <summary>Create a new export set.</summary>
    public ExportSet(string[] functions, string[] cmdlets, string[] aliases)
    { Functions = functions; Cmdlets = cmdlets; Aliases = aliases; }
}

