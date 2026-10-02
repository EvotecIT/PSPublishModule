using System.Management.Automation;

namespace BinaryDocInheritedBase;

/// <summary>Provides shared parameters for documentation inheritance tests.</summary>
public abstract class DocumentationMetadataContractCommandBase : PSCmdlet
{
    /// <summary>Inherited label documented in a separate declaring assembly.</summary>
    [Parameter]
    public string InheritedLabel { get; set; } = string.Empty;
}

/// <summary>Provides a shared parameter inherited through a closed generic cmdlet base.</summary>
/// <typeparam name="T">The consumer's item type.</typeparam>
public abstract class GenericDocumentationMetadataContractCommandBase<T> : DocumentationMetadataContractCommandBase
{
    /// <summary>Generic-base label documented in a separate declaring assembly.</summary>
    [Parameter]
    public string GenericLabel { get; set; } = string.Empty;
}
