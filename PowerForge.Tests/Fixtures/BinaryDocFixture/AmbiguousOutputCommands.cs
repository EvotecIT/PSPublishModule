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
}
