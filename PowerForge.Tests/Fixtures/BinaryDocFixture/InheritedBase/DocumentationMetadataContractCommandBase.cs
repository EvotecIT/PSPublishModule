using System.Management.Automation;

namespace BinaryDocInheritedBase;

/// <summary>Provides shared parameters for documentation inheritance tests.</summary>
public abstract class DocumentationMetadataContractCommandBase : PSCmdlet
{
    /// <summary>Inherited label documented in a separate declaring assembly.</summary>
    [Parameter]
    public string InheritedLabel { get; set; } = string.Empty;
}
