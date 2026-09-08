namespace PowerForge;

/// <summary>Describes native parameter binding for a function whose body is emitted as CLR code.</summary>
/// <remarks>This is a PowerShell-hosted contract and is unavailable to runtime-free targets.</remarks>
public sealed class PowerShellNativeFunctionBinding
{
    /// <summary>Creates the immutable parameter declaration consumed by the native function host.</summary>
    public PowerShellNativeFunctionBinding(string parameterDeclaration)
        : this(parameterDeclaration, System.Array.Empty<string>()) { }

    /// <summary>Creates native metadata with names of unconstrained invocation-local storage slots.</summary>
    public PowerShellNativeFunctionBinding(string parameterDeclaration, System.Collections.Generic.IEnumerable<string> localNames)
    {
        ParameterDeclaration = parameterDeclaration ?? throw new System.ArgumentNullException(nameof(parameterDeclaration));
        LocalNames = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(localNames));
    }

    /// <summary>Authored parameter metadata, including defaults and attributes, without body statements.</summary>
    public string ParameterDeclaration { get; }

    /// <summary>Local slots allocated by the native host without initializing or executing authored body statements.</summary>
    public System.Collections.Generic.IReadOnlyList<string> LocalNames { get; }
}
