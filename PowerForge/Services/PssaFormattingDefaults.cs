using System.Text.Json;

namespace PowerForge;

internal static class PssaFormattingDefaults
{
    private static readonly Lazy<string> DefaultSettingsJsonLazy = new(BuildDefaultSettingsJson);

    public static string DefaultSettingsJson => DefaultSettingsJsonLazy.Value;

    public static string SerializeSettings(FormatterSettingsOptions? settings)
        => settings is null ? DefaultSettingsJson : JsonSerializer.Serialize(settings);

    private static string BuildDefaultSettingsJson()
    {
        var settings = new FormatterSettingsOptions
        {
            IncludeRules = new[]
            {
                "PSPlaceOpenBrace",
                "PSPlaceCloseBrace",
                "PSUseConsistentWhitespace",
                "PSUseConsistentIndentation",
                "PSAlignAssignmentStatement",
                "PSUseCorrectCasing"
            },
            Rules = new FormatterRulesOptions
            {
                PSPlaceOpenBrace = new BraceRuleOptions
                {
                    Enable = true,
                    OnSameLine = true,
                    NewLineAfter = true,
                    IgnoreOneLineBlock = true
                },
                PSPlaceCloseBrace = new CloseBraceRuleOptions
                {
                    Enable = true,
                    NewLineAfter = false,
                    IgnoreOneLineBlock = true,
                    NoEmptyLineBefore = false
                },
                PSUseConsistentIndentation = new IndentationRuleOptions
                {
                    Enable = true,
                    Kind = "space",
                    PipelineIndentation = "IncreaseIndentationAfterEveryPipeline",
                    IndentationSize = 4
                },
                PSUseConsistentWhitespace = new WhitespaceRuleOptions
                {
                    Enable = true,
                    CheckInnerBrace = true,
                    CheckOpenBrace = true,
                    CheckOpenParen = true,
                    CheckOperator = true,
                    CheckPipe = true,
                    CheckSeparator = true
                },
                PSAlignAssignmentStatement = new AlignAssignmentRuleOptions
                {
                    Enable = true,
                    CheckHashtable = true
                },
                PSUseCorrectCasing = new EnableOnlyRuleOptions
                {
                    Enable = true
                }
            }
        };

        return JsonSerializer.Serialize(settings);
    }
}

