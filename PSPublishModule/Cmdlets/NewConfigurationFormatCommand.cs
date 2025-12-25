using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Management.Automation;
using PSPublishModule.Services;

namespace PSPublishModule;

/// <summary>
/// Builds formatting options for code and manifest generation during the build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationFormat")]
public sealed class NewConfigurationFormatCommand : PSCmdlet
{
    /// <summary>Targets to apply formatting to (OnMergePSM1, OnMergePSD1, DefaultPSM1, DefaultPSD1).</summary>
    [Parameter(Mandatory = true)]
    [ValidateSet("OnMergePSM1", "OnMergePSD1", "DefaultPSM1", "DefaultPSD1")]
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
        var settingsCount = 0;

        var merge = new OrderedDictionary();
        var standard = new OrderedDictionary();

        var options = new OrderedDictionary
        {
            ["Merge"] = merge,
            ["Standard"] = standard
        };

        foreach (var apply in ApplyTo)
        {
            var formatting = new OrderedDictionary();

            if (MyInvocation.BoundParameters.ContainsKey(nameof(RemoveComments)))
                formatting["RemoveComments"] = RemoveComments.IsPresent;
            if (MyInvocation.BoundParameters.ContainsKey(nameof(RemoveEmptyLines)))
                formatting["RemoveEmptyLines"] = RemoveEmptyLines.IsPresent;
            if (MyInvocation.BoundParameters.ContainsKey(nameof(RemoveAllEmptyLines)))
                formatting["RemoveAllEmptyLines"] = RemoveAllEmptyLines.IsPresent;
            if (MyInvocation.BoundParameters.ContainsKey(nameof(RemoveCommentsInParamBlock)))
                formatting["RemoveCommentsInParamBlock"] = RemoveCommentsInParamBlock.IsPresent;
            if (MyInvocation.BoundParameters.ContainsKey(nameof(RemoveCommentsBeforeParamBlock)))
                formatting["RemoveCommentsBeforeParamBlock"] = RemoveCommentsBeforeParamBlock.IsPresent;

            var includeRules = new List<string>();
            var rules = new OrderedDictionary();

            if (PlaceOpenBraceEnable.IsPresent)
            {
                includeRules.Add("PSPlaceOpenBrace");
                rules["PSPlaceOpenBrace"] = new OrderedDictionary
                {
                    ["Enable"] = true,
                    ["OnSameLine"] = PlaceOpenBraceOnSameLine.IsPresent,
                    ["NewLineAfter"] = PlaceOpenBraceNewLineAfter.IsPresent,
                    ["IgnoreOneLineBlock"] = PlaceOpenBraceIgnoreOneLineBlock.IsPresent
                };
            }

            if (PlaceCloseBraceEnable.IsPresent)
            {
                includeRules.Add("PSPlaceCloseBrace");
                rules["PSPlaceCloseBrace"] = new OrderedDictionary
                {
                    ["Enable"] = true,
                    ["NewLineAfter"] = PlaceCloseBraceNewLineAfter.IsPresent,
                    ["IgnoreOneLineBlock"] = PlaceCloseBraceIgnoreOneLineBlock.IsPresent,
                    ["NoEmptyLineBefore"] = PlaceCloseBraceNoEmptyLineBefore.IsPresent
                };
            }

            if (UseConsistentIndentationEnable.IsPresent)
            {
                includeRules.Add("PSUseConsistentIndentation");
                rules["PSUseConsistentIndentation"] = new OrderedDictionary
                {
                    ["Enable"] = true,
                    ["Kind"] = UseConsistentIndentationKind,
                    ["PipelineIndentation"] = UseConsistentIndentationPipelineIndentation,
                    ["IndentationSize"] = UseConsistentIndentationIndentationSize
                };
            }

            if (UseConsistentWhitespaceEnable.IsPresent)
            {
                includeRules.Add("PSUseConsistentWhitespace");
                rules["PSUseConsistentWhitespace"] = new OrderedDictionary
                {
                    ["Enable"] = true,
                    ["CheckInnerBrace"] = UseConsistentWhitespaceCheckInnerBrace.IsPresent,
                    ["CheckOpenBrace"] = UseConsistentWhitespaceCheckOpenBrace.IsPresent,
                    ["CheckOpenParen"] = UseConsistentWhitespaceCheckOpenParen.IsPresent,
                    ["CheckOperator"] = UseConsistentWhitespaceCheckOperator.IsPresent,
                    ["CheckPipe"] = UseConsistentWhitespaceCheckPipe.IsPresent,
                    ["CheckSeparator"] = UseConsistentWhitespaceCheckSeparator.IsPresent
                };
            }

            if (AlignAssignmentStatementEnable.IsPresent)
            {
                includeRules.Add("PSAlignAssignmentStatement");
                rules["PSAlignAssignmentStatement"] = new OrderedDictionary
                {
                    ["Enable"] = true,
                    ["CheckHashtable"] = AlignAssignmentStatementCheckHashtable.IsPresent
                };
            }

            if (UseCorrectCasingEnable.IsPresent)
            {
                includeRules.Add("PSUseCorrectCasing");
                rules["PSUseCorrectCasing"] = new OrderedDictionary
                {
                    ["Enable"] = true
                };
            }

            var formatterSettings = new OrderedDictionary
            {
                ["IncludeRules"] = includeRules.ToArray(),
                ["Rules"] = rules
            };
            EmptyValuePruner.RemoveEmptyValues(formatterSettings, recursive: true);
            if (formatterSettings.Count > 0)
                formatting["FormatterSettings"] = formatterSettings;

            if (formatting.Count > 0 || EnableFormatting.IsPresent)
            {
                settingsCount++;
                formatting["Enabled"] = true;

                switch (apply)
                {
                    case "OnMergePSM1":
                        merge["FormatCodePSM1"] = formatting;
                        break;
                    case "OnMergePSD1":
                        merge["FormatCodePSD1"] = formatting;
                        break;
                    case "DefaultPSM1":
                        standard["FormatCodePSM1"] = formatting;
                        break;
                    case "DefaultPSD1":
                        standard["FormatCodePSD1"] = formatting;
                        break;
                    default:
                        throw new PSArgumentException($"Unknown ApplyTo: {apply}");
                }
            }

            if (!string.IsNullOrWhiteSpace(PSD1Style))
            {
                if (apply == "OnMergePSD1")
                {
                    settingsCount++;
                    merge["Style"] = new OrderedDictionary { ["PSD1"] = PSD1Style };
                }
                else if (apply == "DefaultPSD1")
                {
                    settingsCount++;
                    standard["Style"] = new OrderedDictionary { ["PSD1"] = PSD1Style };
                }
            }
        }

        if (settingsCount > 0)
        {
            var output = new OrderedDictionary
            {
                ["Type"] = "Formatting",
                ["Options"] = options
            };
            WriteObject(output);
        }
    }
}

