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
        : this(parameterDeclaration, localNames, System.Array.Empty<string>()) { }

    /// <summary>Creates native storage metadata, including type constraints for optimized local slots.</summary>
    public PowerShellNativeFunctionBinding(string parameterDeclaration, System.Collections.Generic.IEnumerable<string> localNames,
        System.Collections.Generic.IEnumerable<string> localTypeDeclarations)
        : this(parameterDeclaration, localNames, localTypeDeclarations, false, false, true, false) { }

    /// <summary>Creates native storage and clause metadata for separately invoked compiled lifecycle blocks.</summary>
    public PowerShellNativeFunctionBinding(string parameterDeclaration, System.Collections.Generic.IEnumerable<string> localNames,
        System.Collections.Generic.IEnumerable<string> localTypeDeclarations, bool hasBegin, bool hasProcess, bool hasEnd, bool hasClean)
    {
        ParameterDeclaration = parameterDeclaration ?? throw new System.ArgumentNullException(nameof(parameterDeclaration));
        LocalNames = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(localNames));
        LocalTypeDeclarations = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(localTypeDeclarations));
        HasBegin = hasBegin;
        HasProcess = hasProcess;
        HasEnd = hasEnd;
        HasClean = hasClean;
    }

    /// <summary>Whether the native invocation has a compiled begin block.</summary>
    public bool HasBegin { get; }
    /// <summary>Whether the native invocation has a compiled process block.</summary>
    public bool HasProcess { get; }
    /// <summary>Whether the native invocation has a compiled end block.</summary>
    public bool HasEnd { get; }
    /// <summary>Whether the native invocation has a compiled clean block.</summary>
    public bool HasClean { get; }

    /// <summary>Authored parameter metadata, including defaults and attributes, without body statements.</summary>
    public string ParameterDeclaration { get; }

    /// <summary>Local slots allocated by the native host without initializing or executing authored body statements.</summary>
    public System.Collections.Generic.IReadOnlyList<string> LocalNames { get; }

    /// <summary>Authored type-only assignment targets used to allocate optimized slots; no initializers execute.</summary>
    public System.Collections.Generic.IReadOnlyList<string> LocalTypeDeclarations { get; }
}
