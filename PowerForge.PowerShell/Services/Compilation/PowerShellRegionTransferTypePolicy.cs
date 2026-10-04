namespace PowerForge;

/// <summary>
/// Defines CLR values that can cross a retained PowerShell and typed-region boundary without
/// changing their observable pipeline shape. Mutable reference values are admitted only where
/// the selector proves fresh initialization or read-only passthrough; this policy does not grant
/// mutation authority by itself.
/// </summary>
internal static class PowerShellRegionTransferTypePolicy
{
    internal static bool IsSupported(PowerShellTypeFact type)
        => type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble ||
           IsClosedValueAlternative(type) || IsSupported(type.ClrType);

    internal static bool IsSupported(Type type)
        => Describe(type).Supported;

    internal static PowerShellRegionTransferContract Describe(
        Type type,
        PowerShellRegionTransferDirection direction = PowerShellRegionTransferDirection.Unspecified,
        PowerShellRegionTransferOwnership ownership = PowerShellRegionTransferOwnership.Unspecified,
        PowerShellRegionTransferMutation mutation = PowerShellRegionTransferMutation.None)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        var shape = GetShape(type);
        var element = GetElementContract(type, shape);
        var output = shape switch
        {
            PowerShellRegionTransferShape.StableScalar or PowerShellRegionTransferShape.AtomicMap =>
                PowerShellRegionTransferOutputBehavior.Atomic,
            PowerShellRegionTransferShape.StableScalarVector or PowerShellRegionTransferShape.ListSequence =>
                PowerShellRegionTransferOutputBehavior.EnumerateOneLevel,
            _ => PowerShellRegionTransferOutputBehavior.None
        };
        var compiledFreshVector = shape == PowerShellRegionTransferShape.StableScalarVector &&
                                  direction == PowerShellRegionTransferDirection.LiveOut &&
                                  ownership == PowerShellRegionTransferOwnership.GuardedFresh &&
                                  mutation == PowerShellRegionTransferMutation.CompiledOwned;
        var supported = shape != PowerShellRegionTransferShape.Unsupported &&
                        element != PowerShellRegionTransferElementContract.Unsupported &&
                        (mutation != PowerShellRegionTransferMutation.CompiledOwned || compiledFreshVector);
        return new PowerShellRegionTransferContract(
            shape,
            element,
            direction,
            ownership,
            output,
            mutation,
            compiledFreshVector
                ? PowerShellRegionMutationLifetime.CompiledFreshUntilTransfer
                : PowerShellRegionMutationLifetime.None,
            supported);
    }

    /// <summary>
    /// Describes an authored return boundary that terminates the function without transferring a
    /// CLR value or writing a PowerShell success record. This is intentionally separate from
    /// <see cref="Describe(Type, PowerShellRegionTransferDirection, PowerShellRegionTransferOwnership, PowerShellRegionTransferMutation)"/>
    /// so <see cref="System.Void"/> can never become a valid live-in or live-out storage type.
    /// </summary>
    internal static PowerShellRegionTransferContract DescribeNoValue()
        => new(
            PowerShellRegionTransferShape.NoValue,
            PowerShellRegionTransferElementContract.None,
            PowerShellRegionTransferDirection.TerminalSuccess,
            PowerShellRegionTransferOwnership.Unspecified,
            PowerShellRegionTransferOutputBehavior.None,
            PowerShellRegionTransferMutation.None,
            PowerShellRegionMutationLifetime.None,
            supported: true);

    /// <summary>
    /// Describes a statically known null expression that PowerShell preserves as one success record.
    /// The dedicated contract prevents ordinary <see cref="System.Object"/> values from entering the
    /// closed region ABI while retaining the observable difference from AutomationNull/no output.
    /// </summary>
    internal static PowerShellRegionTransferContract DescribeNullValue()
        => new(
            PowerShellRegionTransferShape.NullValue,
            PowerShellRegionTransferElementContract.None,
            PowerShellRegionTransferDirection.TerminalSuccess,
            PowerShellRegionTransferOwnership.Unspecified,
            PowerShellRegionTransferOutputBehavior.Atomic,
            PowerShellRegionTransferMutation.None,
            PowerShellRegionMutationLifetime.None,
            supported: true);

    /// <summary>Describes a compiler-owned envelope whose exact authored alternatives remain closed.</summary>
    internal static PowerShellRegionTransferContract DescribeClosedValueAlternative(
        PowerShellRegionTransferDirection direction,
        PowerShellRegionTransferOwnership ownership,
        PowerShellRegionTransferMutation mutation)
        => new(
            PowerShellRegionTransferShape.ClosedValueAlternative,
            PowerShellRegionTransferElementContract.StableScalar,
            direction,
            ownership,
            PowerShellRegionTransferOutputBehavior.Atomic,
            mutation,
            PowerShellRegionMutationLifetime.None,
            supported: mutation != PowerShellRegionTransferMutation.CompiledOwned);

    internal static bool IsAtomicDictionaryReference(Type type)
        => type == typeof(System.Collections.Hashtable) ||
           type == typeof(System.Collections.Specialized.OrderedDictionary);

    internal static bool IsClosedValueAlternative(PowerShellTypeFact type)
    {
        if (type.ClrType != typeof(PowerForge.Generated.Runtime.PowerShellRegionValueAlternative) ||
            type.ClosedAlternativeTypes.Count != 2)
            return false;
        var scalar = type.ClosedAlternativeTypes.SingleOrDefault(PowerShellStableScalarTypePolicy.IsSupported);
        var vector = type.ClosedAlternativeTypes.SingleOrDefault(candidate =>
            candidate.IsArray && candidate.GetArrayRank() == 1 && candidate.GetElementType() is { } element &&
            candidate == element.MakeArrayType() && PowerShellStableScalarTypePolicy.IsSupported(element));
        return scalar is not null && vector?.GetElementType() == scalar &&
               type.ClosedAlternativeTypes.Distinct().Count() == 2;
    }

    private static PowerShellRegionTransferShape GetShape(Type type)
    {
        if (PowerShellStableScalarTypePolicy.IsSupported(type))
            return PowerShellRegionTransferShape.StableScalar;
        if (IsAtomicDictionaryReference(type))
            return PowerShellRegionTransferShape.AtomicMap;
        if (IsStableScalarVector(type))
            return PowerShellRegionTransferShape.StableScalarVector;
        if (IsClosedListReference(type))
            return PowerShellRegionTransferShape.ListSequence;
        return PowerShellRegionTransferShape.Unsupported;
    }

    private static PowerShellRegionTransferElementContract GetElementContract(
        Type type,
        PowerShellRegionTransferShape shape)
        => shape switch
        {
            PowerShellRegionTransferShape.StableScalar => PowerShellRegionTransferElementContract.StableScalar,
            PowerShellRegionTransferShape.AtomicMap => PowerShellRegionTransferElementContract.OpaqueReference,
            PowerShellRegionTransferShape.StableScalarVector => PowerShellRegionTransferElementContract.StableScalar,
            PowerShellRegionTransferShape.ListSequence when TryGetListElementType(type, out var element) &&
                                                            PowerShellStableScalarTypePolicy.IsSupported(element) =>
                PowerShellRegionTransferElementContract.StableScalar,
            PowerShellRegionTransferShape.ListSequence => PowerShellRegionTransferElementContract.OpaqueReference,
            _ => PowerShellRegionTransferElementContract.Unsupported
        };

    private static bool IsStableScalarVector(Type type)
        => type.IsArray && type.GetArrayRank() == 1 && type.GetElementType() is { } element &&
           type == element.MakeArrayType() && PowerShellStableScalarTypePolicy.IsSupported(element);

    private static bool IsClosedListReference(Type type)
        => type == typeof(Array) || type == typeof(System.Collections.ArrayList) ||
           type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    private static bool TryGetListElementType(Type type, out Type element)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            element = type.GetGenericArguments()[0];
            return true;
        }
        element = typeof(object);
        return false;
    }
}
