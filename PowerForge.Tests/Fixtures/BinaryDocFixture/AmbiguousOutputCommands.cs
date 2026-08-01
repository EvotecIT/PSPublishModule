extern alias DistinctA;
extern alias DistinctB;

using System.Management.Automation;

namespace BinaryDocFixture.OutputA
{
    /// <summary>Represents the first result shape.</summary>
    public sealed class Result
    {
    }

    /// <summary>Represents a distinct CLR type whose name differs only by case.</summary>
    public sealed class RESULT
    {
    }
}

namespace BinaryDocFixture.OutputB
{
    /// <summary>Represents the second result shape.</summary>
    public sealed class Result
    {
    }
}

namespace BinaryDocFixture.CanonicalOutput
{
    /// <summary>Represents a qualified output used to verify canonical casing.</summary>
    public sealed class Result
    {
    }
}

namespace BinaryDocFixture.NestedOutputs
{
    /// <summary>Contains a nested result type whose C# and CLR names differ.</summary>
    public sealed class Outer
    {
        /// <summary>Represents a nested result shape.</summary>
        public sealed class Result
        {
        }
    }
}

namespace BinaryDocFixture
{
    /// <summary>Uses authored external help as its output contract.</summary>
    [Cmdlet(VerbsCommon.Get, "BinaryDocAuthoredOutput")]
    public sealed class GetBinaryDocAuthoredOutputCommand : PSCmdlet
    {
    }

    /// <summary>Returns one qualified output type while stale help names another.</summary>
    [Cmdlet(VerbsCommon.Get, "BinaryDocConflictingOutput")]
    [OutputType(typeof(OutputA.Result))]
    public sealed class GetBinaryDocConflictingOutputCommand : PSCmdlet
    {
    }

    /// <summary>Returns distinct output types that share or case-collide on a short name.</summary>
    [Cmdlet(VerbsCommon.Get, "BinaryDocAmbiguousOutputs")]
    [OutputType(typeof(OutputA.Result), typeof(OutputA.RESULT), typeof(OutputB.Result))]
    public sealed class GetBinaryDocAmbiguousOutputsCommand : PSCmdlet
    {
    }

    /// <summary>Matches an authored qualified output whose casing differs.</summary>
    [Cmdlet(VerbsCommon.Get, "BinaryDocCaseInsensitiveOutput")]
    [OutputType(typeof(CanonicalOutput.Result))]
    public sealed class GetBinaryDocCaseInsensitiveOutputCommand : PSCmdlet
    {
    }

    /// <summary>Matches a nested output authored with normal C# type spelling.</summary>
    [Cmdlet(VerbsCommon.Get, "BinaryDocNestedOutput")]
    [OutputType(typeof(NestedOutputs.Outer.Result))]
    public sealed class GetBinaryDocNestedOutputCommand : PSCmdlet
    {
    }

    /// <summary>Returns assembly-distinct CLR types that share one namespace-qualified display name.</summary>
    [Cmdlet(VerbsCommon.Get, "BinaryDocAssemblyDistinctOutputs")]
    [OutputType(
        typeof(DistinctA::BinaryDocFixture.AssemblyDistinct.SameResult),
        typeof(DistinctB::BinaryDocFixture.AssemblyDistinct.SameResult))]
    public sealed class GetBinaryDocAssemblyDistinctOutputsCommand : PSCmdlet
    {
    }

    /// <summary>Returns an open generic output whose CLR name contains a backtick.</summary>
    [Cmdlet(VerbsCommon.Get, "BinaryDocOpenGenericOutput")]
    [OutputType(typeof(System.Collections.Generic.List<>))]
    public sealed class GetBinaryDocOpenGenericOutputCommand : PSCmdlet
    {
    }
}
