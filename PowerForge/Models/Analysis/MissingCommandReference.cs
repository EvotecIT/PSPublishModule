namespace PowerForge;

/// <summary>
/// Represents a command reference discovered during missing-function analysis in a host-neutral form.
/// </summary>
public sealed class MissingCommandReference
{
    /// <summary>The referenced command name.</summary>
    public string Name { get; }

    /// <summary>The command source (module/snap-in), when available.</summary>
    public string Source { get; }

    /// <summary>The resolved command type as a string; empty when unresolved.</summary>
    public string CommandType { get; }

    /// <summary>Whether the resolved command was an alias.</summary>
    public bool IsAlias { get; }

    /// <summary>Whether the command was resolved from a private module scope.</summary>
    public bool IsPrivate { get; }

    /// <summary>Error message when resolution failed; empty when resolved.</summary>
    public string Error { get; }

    /// <summary>
    /// Creates a new <see cref="MissingCommandReference"/> instance.
    /// </summary>
    public MissingCommandReference(
        string name,
        string source,
        string commandType,
        bool isAlias,
        bool isPrivate,
        string error)
    {
        Name = name ?? string.Empty;
        Source = source ?? string.Empty;
        CommandType = commandType ?? string.Empty;
        IsAlias = isAlias;
        IsPrivate = isPrivate;
        Error = error ?? string.Empty;
    }
}
