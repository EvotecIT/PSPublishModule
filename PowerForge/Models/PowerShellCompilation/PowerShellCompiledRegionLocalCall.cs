namespace PowerForge;

/// <summary>One closed local-call result contract required by a promoted typed region.</summary>
public sealed class PowerShellCompiledRegionLocalCall
{
    /// <summary>Creates immutable local-call closure evidence.</summary>
    public PowerShellCompiledRegionLocalCall(
        string sourceName,
        IReadOnlyList<string>? parameterTypes,
        string loweredReturnType,
        string projectedReturnType,
        PowerShellRegionTransferContract resultContract)
    {
        SourceName = sourceName ?? string.Empty;
        ParameterTypes = Array.AsReadOnly((parameterTypes ?? Array.Empty<string>()).Select(static type => type ?? string.Empty).ToArray());
        LoweredReturnType = loweredReturnType ?? string.Empty;
        ProjectedReturnType = projectedReturnType ?? string.Empty;
        ResultContract = resultContract ?? throw new ArgumentNullException(nameof(resultContract));
    }

    /// <summary>Authored local function name resolved by the canonical binder.</summary>
    public string SourceName { get; }

    /// <summary>Exact CLR parameter types in canonical binding order.</summary>
    public IReadOnlyList<string> ParameterTypes { get; }

    /// <summary>Exact CLR result type produced by the compiler's closed intrinsic lowering.</summary>
    public string LoweredReturnType { get; }

    /// <summary>Exact CLR value type observed by the consuming region.</summary>
    public string ProjectedReturnType { get; }

    /// <summary>Cardinality, ownership, enumeration, and mutation contract for the projected record.</summary>
    public PowerShellRegionTransferContract ResultContract { get; }
}
