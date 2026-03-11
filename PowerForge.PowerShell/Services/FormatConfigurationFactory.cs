using System;
using System.Collections.Generic;

namespace PowerForge;

internal sealed class FormatConfigurationFactory
{
    public ConfigurationFormattingSegment? Create(FormatConfigurationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var settingsCount = 0;
        var options = new FormattingOptions();

        foreach (var apply in request.ApplyTo ?? Array.Empty<string>())
        {
            var hasFormattingSettings = false;
            var formatting = new FormatCodeOptions
            {
                Sort = request.Sort
            };

            if (!string.IsNullOrWhiteSpace(request.Sort))
                hasFormattingSettings = true;

            if (request.RemoveCommentsSpecified)
            {
                formatting.RemoveComments = request.RemoveComments;
                hasFormattingSettings = true;
            }
            if (request.RemoveEmptyLinesSpecified)
            {
                formatting.RemoveEmptyLines = request.RemoveEmptyLines;
                hasFormattingSettings = true;
            }
            if (request.RemoveAllEmptyLinesSpecified)
            {
                formatting.RemoveAllEmptyLines = request.RemoveAllEmptyLines;
                hasFormattingSettings = true;
            }
            if (request.RemoveCommentsInParamBlockSpecified)
            {
                formatting.RemoveCommentsInParamBlock = request.RemoveCommentsInParamBlock;
                hasFormattingSettings = true;
            }
            if (request.RemoveCommentsBeforeParamBlockSpecified)
            {
                formatting.RemoveCommentsBeforeParamBlock = request.RemoveCommentsBeforeParamBlock;
                hasFormattingSettings = true;
            }

            var includeRules = new List<string>();
            var rules = new FormatterRulesOptions();

            if (request.PlaceOpenBraceEnable)
            {
                includeRules.Add("PSPlaceOpenBrace");
                rules.PSPlaceOpenBrace = new BraceRuleOptions
                {
                    Enable = true,
                    OnSameLine = request.PlaceOpenBraceOnSameLine,
                    NewLineAfter = request.PlaceOpenBraceNewLineAfter,
                    IgnoreOneLineBlock = request.PlaceOpenBraceIgnoreOneLineBlock
                };
                hasFormattingSettings = true;
            }

            if (request.PlaceCloseBraceEnable)
            {
                includeRules.Add("PSPlaceCloseBrace");
                rules.PSPlaceCloseBrace = new CloseBraceRuleOptions
                {
                    Enable = true,
                    NewLineAfter = request.PlaceCloseBraceNewLineAfter,
                    IgnoreOneLineBlock = request.PlaceCloseBraceIgnoreOneLineBlock,
                    NoEmptyLineBefore = request.PlaceCloseBraceNoEmptyLineBefore
                };
                hasFormattingSettings = true;
            }

            if (request.UseConsistentIndentationEnable)
            {
                includeRules.Add("PSUseConsistentIndentation");
                rules.PSUseConsistentIndentation = new IndentationRuleOptions
                {
                    Enable = true,
                    Kind = request.UseConsistentIndentationKind,
                    PipelineIndentation = request.UseConsistentIndentationPipelineIndentation,
                    IndentationSize = request.UseConsistentIndentationIndentationSize
                };
                hasFormattingSettings = true;
            }

            if (request.UseConsistentWhitespaceEnable)
            {
                includeRules.Add("PSUseConsistentWhitespace");
                rules.PSUseConsistentWhitespace = new WhitespaceRuleOptions
                {
                    Enable = true,
                    CheckInnerBrace = request.UseConsistentWhitespaceCheckInnerBrace,
                    CheckOpenBrace = request.UseConsistentWhitespaceCheckOpenBrace,
                    CheckOpenParen = request.UseConsistentWhitespaceCheckOpenParen,
                    CheckOperator = request.UseConsistentWhitespaceCheckOperator,
                    CheckPipe = request.UseConsistentWhitespaceCheckPipe,
                    CheckSeparator = request.UseConsistentWhitespaceCheckSeparator
                };
                hasFormattingSettings = true;
            }

            if (request.AlignAssignmentStatementEnable)
            {
                includeRules.Add("PSAlignAssignmentStatement");
                rules.PSAlignAssignmentStatement = new AlignAssignmentRuleOptions
                {
                    Enable = true,
                    CheckHashtable = request.AlignAssignmentStatementCheckHashtable
                };
                hasFormattingSettings = true;
            }

            if (request.UseCorrectCasingEnable)
            {
                includeRules.Add("PSUseCorrectCasing");
                rules.PSUseCorrectCasing = new EnableOnlyRuleOptions
                {
                    Enable = true
                };
                hasFormattingSettings = true;
            }

            if (includeRules.Count > 0)
            {
                formatting.FormatterSettings = new FormatterSettingsOptions
                {
                    IncludeRules = includeRules.ToArray(),
                    Rules = rules
                };
                hasFormattingSettings = true;
            }

            if (hasFormattingSettings || request.EnableFormatting)
            {
                settingsCount++;
                formatting.Enabled = true;

                switch (apply)
                {
                    case "OnMergePSM1":
                        options.Merge.FormatCodePSM1 = formatting;
                        break;
                    case "OnMergePSD1":
                        options.Merge.FormatCodePSD1 = formatting;
                        break;
                    case "DefaultPSM1":
                        options.Standard.FormatCodePSM1 = formatting;
                        break;
                    case "DefaultPS1":
                        options.Standard.FormatCodePS1 = formatting;
                        break;
                    case "DefaultPSD1":
                        options.Standard.FormatCodePSD1 = formatting;
                        break;
                    default:
                        throw new ArgumentException($"Unknown ApplyTo: {apply}", nameof(request));
                }
            }

            if (!string.IsNullOrWhiteSpace(request.PSD1Style))
            {
                if (apply == "OnMergePSD1")
                {
                    settingsCount++;
                    options.Merge.Style = new FormattingStyleOptions { PSD1 = request.PSD1Style };
                }
                else if (apply == "DefaultPSD1")
                {
                    settingsCount++;
                    options.Standard.Style = new FormattingStyleOptions { PSD1 = request.PSD1Style };
                }
            }
        }

        if (request.UpdateProjectRoot)
        {
            options.UpdateProjectRoot = true;
            settingsCount++;
        }

        return settingsCount > 0
            ? new ConfigurationFormattingSegment { Options = options }
            : null;
    }
}
