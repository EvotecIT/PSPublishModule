using System.Management.Automation;

namespace BinaryDocFixture.OutputA
{
    /// <summary>Represents the first result shape.</summary>
    public sealed class Result
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

    /// <summary>Returns two distinct output types that share a short name.</summary>
    [Cmdlet(VerbsCommon.Get, "BinaryDocAmbiguousOutputs")]
    [OutputType(typeof(OutputA.Result), typeof(OutputB.Result))]
    public sealed class GetBinaryDocAmbiguousOutputsCommand : PSCmdlet
    {
    }
}
