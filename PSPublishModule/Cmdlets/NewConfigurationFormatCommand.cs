using System;
using System.Collections.Generic;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Builds formatting options for code and manifest generation during the build.
/// </summary>
/// <remarks>
/// <para>
/// Produces a formatting configuration segment used by the build pipeline to normalize generated output
/// (merged PSM1/PSD1) and optionally apply formatting back to the project root.
/// </para>
/// </remarks>
/// <example>
/// <summary>Remove comments and normalize whitespace during merge</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationFormat -ApplyTo OnMergePSM1,OnMergePSD1 -RemoveComments -RemoveEmptyLines</code>
/// <para>Formats the merged module output and removes comments while keeping readability.</para>
/// </example>
/// <example>
/// <summary>Also update files in the project root</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationFormat -ApplyTo DefaultPSM1,DefaultPSD1 -EnableFormatting -UpdateProjectRoot</code>
/// <para>Applies formatting rules to the project sources as well as generated output.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationFormat")]
public sealed class NewConfigurationFormatCommand : PSCmdlet
{
    /// <summary>Targets to apply formatting to (OnMergePSM1, OnMergePSD1, DefaultPS1, DefaultPSM1, DefaultPSD1).</summary>
    [Parameter(Mandatory = true)]
    [ValidateSet("OnMergePSM1", "OnMergePSD1", "DefaultPS1", "DefaultPSM1", "DefaultPSD1")]
    public string[] ApplyTo { get; set; } = Array.Empty<string>();

    /// <summary>Enables formatting for the chosen ApplyTo targets even if no specific rule switches are provided.</summary>
    [Parameter]
    public SwitchParameter EnableFormatting { get; set; }

    /// <summary>Optional ordering hint for internal processing. Accepts None, Asc, or Desc.</summary>
    [Parameter]
    [ValidateSet("None", "Asc", "Desc")]
    public string? Sort { get; set; }

    /// <summary>Remove comments in the formatted output.</summary>
    [Parameter]
    public SwitchParameter RemoveComments { get; set; }

    /// <summary>Remove empty lines while preserving readability.</summary>
    [Parameter]
    public SwitchParameter RemoveEmptyLines { get; set; }

    /// <summary>Remove all empty lines (more aggressive than RemoveEmptyLines).</summary>
    [Parameter]
    public SwitchParameter RemoveAllEmptyLines { get; set; }

    /// <summary>Remove comments within the param() block.</summary>
    [Parameter]
    public SwitchParameter RemoveCommentsInParamBlock { get; set; }

    /// <summary>Remove comments that appear immediately before the param() block.</summary>
    [Parameter]
    public SwitchParameter RemoveCommentsBeforeParamBlock { get; set; }

    /// <summary>When set, formats PowerShell sources in the project root in addition to staging output.</summary>
    [Parameter]
    public SwitchParameter UpdateProjectRoot { get; set; }

    /// <summary>Enable PSPlaceOpenBrace rule and configure its behavior.</summary>
    [Parameter]
    public SwitchParameter PlaceOpenBraceEnable { get; set; }

    /// <summary>For PSPlaceOpenBrace: place opening brace on the same line.</summary>
    [Parameter]
    public SwitchParameter PlaceOpenBraceOnSameLine { get; set; }

    /// <summary>For PSPlaceOpenBrace: enforce a new line after the opening brace.</summary>
    [Parameter]
    public SwitchParameter PlaceOpenBraceNewLineAfter { get; set; }

    /// <summary>For PSPlaceOpenBrace: ignore single-line blocks.</summary>
    [Parameter]
    public SwitchParameter PlaceOpenBraceIgnoreOneLineBlock { get; set; }

    /// <summary>Enable PSPlaceCloseBrace rule and configure its behavior.</summary>
    [Parameter]
    public SwitchParameter PlaceCloseBraceEnable { get; set; }

    /// <summary>For PSPlaceCloseBrace: enforce a new line after the closing brace.</summary>
    [Parameter]
    public SwitchParameter PlaceCloseBraceNewLineAfter { get; set; }

    /// <summary>For PSPlaceCloseBrace: ignore single-line blocks.</summary>
    [Parameter]
    public SwitchParameter PlaceCloseBraceIgnoreOneLineBlock { get; set; }

    /// <summary>For PSPlaceCloseBrace: do not allow an empty line before a closing brace.</summary>
    [Parameter]
    public SwitchParameter PlaceCloseBraceNoEmptyLineBefore { get; set; }

    /// <summary>Enable PSUseConsistentIndentation rule and configure its behavior.</summary>
    [Parameter]
    public SwitchParameter UseConsistentIndentationEnable { get; set; }

    /// <summary>Indentation style for PSUseConsistentIndentation: space or tab.</summary>
    [Parameter]
    [ValidateSet("space", "tab")]
    public string? UseConsistentIndentationKind { get; set; }

    /// <summary>Pipeline indentation mode for PSUseConsistentIndentation.</summary>
    [Parameter]
    [ValidateSet("IncreaseIndentationAfterEveryPipeline", "NoIndentation")]
    public string? UseConsistentIndentationPipelineIndentation { get; set; }

    /// <summary>Number of spaces for indentation when Kind is space.</summary>
    [Parameter]
    public int UseConsistentIndentationIndentationSize { get; set; }

    /// <summary>Enable PSUseConsistentWhitespace rule and configure which elements to check.</summary>
    [Parameter]
    public SwitchParameter UseConsistentWhitespaceEnable { get; set; }

    /// <summary>For PSUseConsistentWhitespace: check inner brace spacing.</summary>
    [Parameter]
    public SwitchParameter UseConsistentWhitespaceCheckInnerBrace { get; set; }

    /// <summary>For PSUseConsistentWhitespace: check open brace spacing.</summary>
    [Parameter]
    public SwitchParameter UseConsistentWhitespaceCheckOpenBrace { get; set; }

    /// <summary>For PSUseConsistentWhitespace: check open parenthesis spacing.</summary>
    [Parameter]
    public SwitchParameter UseConsistentWhitespaceCheckOpenParen { get; set; }

    /// <summary>For PSUseConsistentWhitespace: check operator spacing.</summary>
    [Parameter]
    public SwitchParameter UseConsistentWhitespaceCheckOperator { get; set; }

    /// <summary>For PSUseConsistentWhitespace: check pipeline operator spacing.</summary>
    [Parameter]
    public SwitchParameter UseConsistentWhitespaceCheckPipe { get; set; }

    /// <summary>For PSUseConsistentWhitespace: check separator (comma) spacing.</summary>
    [Parameter]
    public SwitchParameter UseConsistentWhitespaceCheckSeparator { get; set; }

    /// <summary>Enable PSAlignAssignmentStatement rule and optionally check hashtable alignment.</summary>
    [Parameter]
    public SwitchParameter AlignAssignmentStatementEnable { get; set; }

    /// <summary>For PSAlignAssignmentStatement: align hashtable assignments.</summary>
    [Parameter]
    public SwitchParameter AlignAssignmentStatementCheckHashtable { get; set; }

    /// <summary>Enable PSUseCorrectCasing rule.</summary>
    [Parameter]
    public SwitchParameter UseCorrectCasingEnable { get; set; }

    /// <summary>Style for generated manifests (PSD1) for the selected ApplyTo targets.</summary>
    [Parameter]
    [ValidateSet("Minimal", "Native")]
    public string? PSD1Style { get; set; }

    /// <summary>Emits formatting configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var factory = new FormatConfigurationFactory();
        var configuration = factory.Create(new FormatConfigurationRequest
        {
            ApplyTo = ApplyTo,
            EnableFormatting = EnableFormatting.IsPresent,
            Sort = Sort,
            RemoveCommentsSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RemoveComments)),
            RemoveComments = RemoveComments.IsPresent,
            RemoveEmptyLinesSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RemoveEmptyLines)),
            RemoveEmptyLines = RemoveEmptyLines.IsPresent,
            RemoveAllEmptyLinesSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RemoveAllEmptyLines)),
            RemoveAllEmptyLines = RemoveAllEmptyLines.IsPresent,
            RemoveCommentsInParamBlockSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RemoveCommentsInParamBlock)),
            RemoveCommentsInParamBlock = RemoveCommentsInParamBlock.IsPresent,
            RemoveCommentsBeforeParamBlockSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RemoveCommentsBeforeParamBlock)),
            RemoveCommentsBeforeParamBlock = RemoveCommentsBeforeParamBlock.IsPresent,
            UpdateProjectRoot = UpdateProjectRoot.IsPresent,
            PlaceOpenBraceEnable = PlaceOpenBraceEnable.IsPresent,
            PlaceOpenBraceOnSameLine = PlaceOpenBraceOnSameLine.IsPresent,
            PlaceOpenBraceNewLineAfter = PlaceOpenBraceNewLineAfter.IsPresent,
            PlaceOpenBraceIgnoreOneLineBlock = PlaceOpenBraceIgnoreOneLineBlock.IsPresent,
            PlaceCloseBraceEnable = PlaceCloseBraceEnable.IsPresent,
            PlaceCloseBraceNewLineAfter = PlaceCloseBraceNewLineAfter.IsPresent,
            PlaceCloseBraceIgnoreOneLineBlock = PlaceCloseBraceIgnoreOneLineBlock.IsPresent,
            PlaceCloseBraceNoEmptyLineBefore = PlaceCloseBraceNoEmptyLineBefore.IsPresent,
            UseConsistentIndentationEnable = UseConsistentIndentationEnable.IsPresent,
            UseConsistentIndentationKind = UseConsistentIndentationKind,
            UseConsistentIndentationPipelineIndentation = UseConsistentIndentationPipelineIndentation,
            UseConsistentIndentationIndentationSize = UseConsistentIndentationIndentationSize,
            UseConsistentWhitespaceEnable = UseConsistentWhitespaceEnable.IsPresent,
            UseConsistentWhitespaceCheckInnerBrace = UseConsistentWhitespaceCheckInnerBrace.IsPresent,
            UseConsistentWhitespaceCheckOpenBrace = UseConsistentWhitespaceCheckOpenBrace.IsPresent,
            UseConsistentWhitespaceCheckOpenParen = UseConsistentWhitespaceCheckOpenParen.IsPresent,
            UseConsistentWhitespaceCheckOperator = UseConsistentWhitespaceCheckOperator.IsPresent,
            UseConsistentWhitespaceCheckPipe = UseConsistentWhitespaceCheckPipe.IsPresent,
            UseConsistentWhitespaceCheckSeparator = UseConsistentWhitespaceCheckSeparator.IsPresent,
            AlignAssignmentStatementEnable = AlignAssignmentStatementEnable.IsPresent,
            AlignAssignmentStatementCheckHashtable = AlignAssignmentStatementCheckHashtable.IsPresent,
            UseCorrectCasingEnable = UseCorrectCasingEnable.IsPresent,
            PSD1Style = PSD1Style
        });

        if (configuration is not null)
            WriteObject(configuration);
    }
}
