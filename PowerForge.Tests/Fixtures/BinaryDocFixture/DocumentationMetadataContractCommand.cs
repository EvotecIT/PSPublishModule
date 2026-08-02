using System.Management.Automation;
using BinaryDocInheritedBase;

namespace BinaryDocFixture;

/// <summary>Returns metadata used to verify binary documentation normalization.</summary>
[Cmdlet(VerbsCommon.Get, "BinaryDocMetadataContract")]
public sealed class GetBinaryDocMetadataContractCommand : DocumentationMetadataContractCommandBase
{
    /// <summary>Optional nullable mode.</summary>
    [Parameter]
    public BinaryDocMode? NullableMode { get; set; }

    /// <summary>Optional multidimensional nullable modes.</summary>
    [Parameter]
    public BinaryDocMode?[,] NullableModeMatrix { get; set; } = new BinaryDocMode?[0, 0];

    /// <summary>Internal transport value that must not appear in public help.</summary>
    [Parameter(DontShow = true)]
    public string HiddenTransport { get; set; } = string.Empty;
}
