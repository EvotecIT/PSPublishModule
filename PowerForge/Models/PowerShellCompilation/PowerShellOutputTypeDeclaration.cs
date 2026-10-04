namespace PowerForge;

/// <summary>One authored OutputType attribute, retained separately from inferred return semantics.</summary>
internal sealed class PowerShellOutputTypeDeclaration
{
    internal PowerShellOutputTypeDeclaration(string[] typeNames, string? parameterSetName, bool useClrTypes)
    {
        TypeNames = typeNames;
        ParameterSetName = parameterSetName ?? string.Empty;
        UseClrTypes = useClrTypes;
    }

    internal string[] TypeNames { get; }
    internal string ParameterSetName { get; }
    internal bool UseClrTypes { get; }
}
