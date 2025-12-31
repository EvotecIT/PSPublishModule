using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PowerForge;

/// <summary>
/// Help/documentation data extracted from PowerShell for markdown generation.
/// </summary>
[DataContract]
internal sealed class DocumentationExtractionPayload
{
    /// <summary>Module name discovered during extraction.</summary>
    [DataMember(Name = "moduleName")]
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Module version from the manifest, when available.</summary>
    [DataMember(Name = "moduleVersion")]
    public string? ModuleVersion { get; set; }

    /// <summary>Module GUID from the manifest, when available.</summary>
    [DataMember(Name = "moduleGuid")]
    public string? ModuleGuid { get; set; }

    /// <summary>Module description from the manifest, when available.</summary>
    [DataMember(Name = "moduleDescription")]
    public string? ModuleDescription { get; set; }

    /// <summary>HelpInfo URI from the manifest (for updatable help), when available.</summary>
    [DataMember(Name = "helpInfoUri")]
    public string? HelpInfoUri { get; set; }

    /// <summary>Project URI (PrivateData.PSData.ProjectUri), when available.</summary>
    [DataMember(Name = "projectUri")]
    public string? ProjectUri { get; set; }

    /// <summary>Extracted command help entries.</summary>
    [DataMember(Name = "commands")]
    public List<DocumentationCommandHelp> Commands { get; set; } = new();       
}

/// <summary>
/// Extracted help information for a single command.
/// </summary>
[DataContract]
internal sealed class DocumentationCommandHelp
{
    /// <summary>Command name (Verb-Noun).</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Command type (Cmdlet/Function).</summary>
    [DataMember(Name = "commandType")]
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// Full name of the implementing .NET type for cmdlets (from <c>Get-Command</c>), when available.
    /// Empty for script functions.
    /// </summary>
    [DataMember(Name = "implementingType")]
    public string? ImplementingType { get; set; }

    /// <summary>
    /// Assembly path for cmdlets (from <c>Get-Command</c>), when available.
    /// Empty for script functions.
    /// </summary>
    [DataMember(Name = "assemblyPath")]
    public string? AssemblyPath { get; set; }

    /// <summary>Default parameter set name, when available.</summary>
    [DataMember(Name = "defaultParameterSet")]
    public string? DefaultParameterSet { get; set; }

    /// <summary>Synopsis text (short description).</summary>
    [DataMember(Name = "synopsis")]
    public string Synopsis { get; set; } = string.Empty;

    /// <summary>Full description text.</summary>
    [DataMember(Name = "description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Syntax blocks (per parameter set).</summary>
    [DataMember(Name = "syntax")]
    public List<DocumentationSyntaxHelp> Syntax { get; set; } = new();

    /// <summary>Parameter documentation entries.</summary>
    [DataMember(Name = "parameters")]
    public List<DocumentationParameterHelp> Parameters { get; set; } = new();

    /// <summary>Examples.</summary>
    [DataMember(Name = "examples")]
    public List<DocumentationExampleHelp> Examples { get; set; } = new();       

    /// <summary>Input types (from Get-Help).</summary>
    [DataMember(Name = "inputs")]
    public List<DocumentationTypeHelp> Inputs { get; set; } = new();

    /// <summary>Return/output types (from Get-Help).</summary>
    [DataMember(Name = "outputs")]
    public List<DocumentationTypeHelp> Outputs { get; set; } = new();

    /// <summary>Related links (from Get-Help).</summary>
    [DataMember(Name = "relatedLinks")]
    public List<DocumentationLinkHelp> RelatedLinks { get; set; } = new();
}

/// <summary>
/// Extracted input/output type help entry.
/// </summary>
[DataContract]
internal sealed class DocumentationTypeHelp
{
    /// <summary>Type name.</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Type description.</summary>
    [DataMember(Name = "description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Extracted related link (text + uri).
/// </summary>
[DataContract]
internal sealed class DocumentationLinkHelp
{
    /// <summary>Link text.</summary>
    [DataMember(Name = "text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Link URI.</summary>
    [DataMember(Name = "uri")]
    public string Uri { get; set; } = string.Empty;
}

/// <summary>
/// Syntax block for a command parameter set.
/// </summary>
[DataContract]
internal sealed class DocumentationSyntaxHelp
{
    /// <summary>Parameter set name.</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>True when this is the default parameter set.</summary>
    [DataMember(Name = "isDefault")]
    public bool IsDefault { get; set; }

    /// <summary>Rendered syntax line.</summary>
    [DataMember(Name = "text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Documentation for a single parameter.
/// </summary>
[DataContract]
internal sealed class DocumentationParameterHelp
{
    /// <summary>Parameter name.</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Parameter type name.</summary>
    [DataMember(Name = "type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Parameter description.</summary>
    [DataMember(Name = "description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Parameter sets where this parameter is used.</summary>
    [DataMember(Name = "parameterSets")]
    public List<string> ParameterSets { get; set; } = new();

    /// <summary>Parameter aliases.</summary>
    [DataMember(Name = "aliases")]
    public List<string> Aliases { get; set; } = new();

    /// <summary>True when the parameter is required.</summary>
    [DataMember(Name = "required")]
    public bool Required { get; set; }

    /// <summary>Position information (Named/0/1/...).</summary>
    [DataMember(Name = "position")]
    public string Position { get; set; } = string.Empty;

    /// <summary>Default value (stringified).</summary>
    [DataMember(Name = "defaultValue")]
    public string DefaultValue { get; set; } = string.Empty;

    /// <summary>Pipeline input text (True/False/ByValue/ByPropertyName).</summary>
    [DataMember(Name = "pipelineInput")]
    public string PipelineInput { get; set; } = string.Empty;

    /// <summary>True when wildcard characters are accepted.</summary>
    [DataMember(Name = "acceptWildcardCharacters")]
    public bool AcceptWildcardCharacters { get; set; }
}

/// <summary>
/// Extracted example for a command.
/// </summary>
[DataContract]
internal sealed class DocumentationExampleHelp
{
    /// <summary>Example title.</summary>
    [DataMember(Name = "title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Example code.</summary>
    [DataMember(Name = "code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Example remarks.</summary>
    [DataMember(Name = "remarks")]
    public string Remarks { get; set; } = string.Empty;
}
