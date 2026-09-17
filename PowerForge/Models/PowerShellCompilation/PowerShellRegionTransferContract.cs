namespace PowerForge;

/// <summary>CLR storage shape crossing a retained PowerShell and typed-region boundary.</summary>
public enum PowerShellRegionTransferShape
{
    /// <summary>No closed transfer shape was proved.</summary>
    Unsupported,
    /// <summary>A value with stable scalar PowerShell output behavior.</summary>
    StableScalar,
    /// <summary>An exact Hashtable or OrderedDictionary reference emitted atomically.</summary>
    AtomicMap,
    /// <summary>A single-dimensional, zero-based array whose element type is a stable scalar.</summary>
    StableScalarVector,
    /// <summary>An exact System.Array, ArrayList, or List&lt;T&gt; reference used without compiled mutation.</summary>
    ListSequence
}

/// <summary>Element-level contract carried by a region transfer.</summary>
public enum PowerShellRegionTransferElementContract
{
    /// <summary>No closed element contract was proved.</summary>
    Unsupported,
    /// <summary>Every element has a compiler-supported stable scalar type.</summary>
    StableScalar,
    /// <summary>Elements are opaque references and are never inspected or mutated by the transfer ABI.</summary>
    OpaqueReference
}

/// <summary>Direction in which a value crosses a retained/typed boundary.</summary>
public enum PowerShellRegionTransferDirection
{
    /// <summary>The direction is analysis-only or not yet assigned.</summary>
    Unspecified,
    /// <summary>The value enters a typed region.</summary>
    LiveIn,
    /// <summary>The value returns to retained PowerShell.</summary>
    LiveOut,
    /// <summary>The value is both read and returned by a typed region.</summary>
    LiveInOut,
    /// <summary>The value is the function's terminal success output.</summary>
    TerminalSuccess
}

/// <summary>Proof that establishes the storage observed at a region boundary.</summary>
public enum PowerShellRegionTransferOwnership
{
    /// <summary>No executable ownership proof was assigned.</summary>
    Unspecified,
    /// <summary>The authored parameter contract owns the incoming reference or value.</summary>
    ParameterBorrowed,
    /// <summary>An authored local type constraint establishes the storage contract.</summary>
    RetainedDefiniteAssignment,
    /// <summary>A guarded typed prefix created fresh invocation-local storage.</summary>
    GuardedFresh,
    /// <summary>An earlier promoted region established the same invocation-local storage.</summary>
    EarlierRegion
}

/// <summary>Observable PowerShell success-output behavior of a transferred value.</summary>
public enum PowerShellRegionTransferOutputBehavior
{
    /// <summary>The value is not emitted as success output at this boundary.</summary>
    None,
    /// <summary>The value is emitted as one atomic record.</summary>
    Atomic,
    /// <summary>PowerShell enumerates the collection by one level.</summary>
    EnumerateOneLevel,
    /// <summary>The authored boundary explicitly suppresses enumeration.</summary>
    NoEnumerate
}

/// <summary>Mutation authority carried by a transferred reference.</summary>
public enum PowerShellRegionTransferMutation
{
    /// <summary>The transferred value is read only.</summary>
    None,
    /// <summary>Only retained PowerShell may mutate the transferred reference.</summary>
    RetainedOnly,
    /// <summary>The typed region may mutate the transferred reference. This is not currently admitted.</summary>
    CompiledOwned
}

/// <summary>Runtime owner of collection enumeration at a retained/typed region boundary.</summary>
public enum PowerShellRegionEnumerationOwner
{
    /// <summary>The transferred value is not enumerated at this boundary.</summary>
    None,
    /// <summary>The retained PowerShell engine owns enumeration and its host-specific semantics.</summary>
    RetainedPowerShell
}

/// <summary>Failure and continuation behavior while a transferred collection is enumerated.</summary>
public enum PowerShellRegionEnumerationFailureBehavior
{
    /// <summary>No collection enumeration occurs at this boundary.</summary>
    None,
    /// <summary>Records already written remain visible and the retained statement owns error continuation.</summary>
    PreservePartialSuccessAndStatementContinuation
}

/// <summary>Owner of an enumerator's lifetime and cleanup at a retained/typed boundary.</summary>
public enum PowerShellRegionEnumeratorLifetime
{
    /// <summary>No enumerator is acquired at this boundary.</summary>
    None,
    /// <summary>The retained PowerShell engine owns acquisition, stopping checks, and disposal.</summary>
    RetainedPowerShell
}

/// <summary>Complete, versioned contract for one retained/typed value crossing.</summary>
public sealed class PowerShellRegionTransferContract
{
    /// <summary>Creates immutable region-transfer metadata.</summary>
    public PowerShellRegionTransferContract(
        PowerShellRegionTransferShape shape,
        PowerShellRegionTransferElementContract elementContract,
        PowerShellRegionTransferDirection direction,
        PowerShellRegionTransferOwnership ownership,
        PowerShellRegionTransferOutputBehavior outputBehavior,
        PowerShellRegionTransferMutation mutation,
        bool supported)
    {
        Shape = shape;
        ElementContract = elementContract;
        Direction = direction;
        Ownership = ownership;
        OutputBehavior = outputBehavior;
        Mutation = mutation;
        Supported = supported;
    }

    /// <summary>Contract schema version.</summary>
    public int SchemaVersion => 2;
    /// <summary>CLR storage shape.</summary>
    public PowerShellRegionTransferShape Shape { get; }
    /// <summary>Element-level contract.</summary>
    public PowerShellRegionTransferElementContract ElementContract { get; }
    /// <summary>Boundary direction.</summary>
    public PowerShellRegionTransferDirection Direction { get; }
    /// <summary>Storage ownership proof.</summary>
    public PowerShellRegionTransferOwnership Ownership { get; }
    /// <summary>Success-output behavior.</summary>
    public PowerShellRegionTransferOutputBehavior OutputBehavior { get; }
    /// <summary>Mutation authority.</summary>
    public PowerShellRegionTransferMutation Mutation { get; }
    /// <summary>Owner that performs one-level output enumeration.</summary>
    public PowerShellRegionEnumerationOwner EnumerationOwner =>
        OutputBehavior == PowerShellRegionTransferOutputBehavior.EnumerateOneLevel
            ? PowerShellRegionEnumerationOwner.RetainedPowerShell
            : PowerShellRegionEnumerationOwner.None;
    /// <summary>Observable behavior when output enumeration fails after zero or more records.</summary>
    public PowerShellRegionEnumerationFailureBehavior EnumerationFailureBehavior =>
        EnumerationOwner == PowerShellRegionEnumerationOwner.RetainedPowerShell
            ? PowerShellRegionEnumerationFailureBehavior.PreservePartialSuccessAndStatementContinuation
            : PowerShellRegionEnumerationFailureBehavior.None;
    /// <summary>Owner of enumeration stopping and cleanup.</summary>
    public PowerShellRegionEnumeratorLifetime EnumeratorLifetime =>
        EnumerationOwner == PowerShellRegionEnumerationOwner.RetainedPowerShell
            ? PowerShellRegionEnumeratorLifetime.RetainedPowerShell
            : PowerShellRegionEnumeratorLifetime.None;
    /// <summary>Whether the complete combination is admitted by the current closed ABI.</summary>
    public bool Supported { get; }
}
