using System.Management.Automation;

namespace PowerForge;

/// <summary>
/// Represents a command reference discovered during missing-function analysis.
/// </summary>
public sealed class MissingFunctionCommand
{
    /// <summary>The referenced command name.</summary>
    public string Name { get; }

    /// <summary>The command source (module/snap-in), when available.</summary>
    public string Source { get; }

    /// <summary>
    /// The command type as a string (for example: Cmdlet, Function, Alias); empty when unresolved.
    /// </summary>
    public string CommandType { get; }

    /// <summary>Whether the resolved command was an alias.</summary>
    public bool IsAlias { get; }

    /// <summary>Whether the command was resolved from a private module scope.</summary>
    public bool IsPrivate { get; }

    /// <summary>Error message when resolution failed; empty when resolved.</summary>
    public string Error { get; }

    /// <summary>
    /// ScriptBlock for function commands, when available; used to build inline helper definitions.
    /// </summary>
    public ScriptBlock? ScriptBlock { get; }

    /// <summary>
    /// Creates a new <see cref="MissingFunctionCommand"/> instance.
    /// </summary>
    public MissingFunctionCommand(
        string name,
        string source,
        string commandType,
        bool isAlias,
        bool isPrivate,
        string error,
        ScriptBlock? scriptBlock)
    {
        Name = name ?? string.Empty;
        Source = source ?? string.Empty;
        CommandType = commandType ?? string.Empty;
        IsAlias = isAlias;
        IsPrivate = isPrivate;
        Error = error ?? string.Empty;
        ScriptBlock = scriptBlock;
    }
}

