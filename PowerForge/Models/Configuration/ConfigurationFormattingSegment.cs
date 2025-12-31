namespace PowerForge;

/// <summary>
/// Configuration segment that describes formatting rules used during legacy build steps.
/// </summary>
public sealed class ConfigurationFormattingSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Formatting";

    /// <summary>Formatting options payload.</summary>
    public FormattingOptions Options { get; set; } = new();
}

/// <summary>
/// Formatting options payload for <see cref="ConfigurationFormattingSegment"/>.
/// </summary>
public sealed class FormattingOptions
{
    /// <summary>Formatting settings applied during merge steps.</summary>
    public FormattingTargetOptions Merge { get; set; } = new();

    /// <summary>Formatting settings applied to standard (non-merge) files.</summary>
    public FormattingTargetOptions Standard { get; set; } = new();

    /// <summary>
    /// When true, formats PowerShell sources in the project root in addition to staging output.
    /// </summary>
    public bool UpdateProjectRoot { get; set; }
}

/// <summary>
/// Formatting settings for a target group (Merge/Standard).
/// </summary>
public sealed class FormattingTargetOptions
{
    /// <summary>Style configuration.</summary>
    public FormattingStyleOptions? Style { get; set; }

    /// <summary>Formatting options for PSM1 code.</summary>
    public FormatCodeOptions? FormatCodePSM1 { get; set; }

    /// <summary>Formatting options for PSD1 code.</summary>
    public FormatCodeOptions? FormatCodePSD1 { get; set; }
}

/// <summary>
/// Style options used by legacy PSD1 generation.
/// </summary>
public sealed class FormattingStyleOptions
{
    /// <summary>PSD1 style name (legacy: Style.PSD1).</summary>
    public string? PSD1 { get; set; }
}

/// <summary>
/// Options controlling Invoke-Formatter behavior (legacy).
/// </summary>
public sealed class FormatCodeOptions
{
    /// <summary>Enable formatting.</summary>
    public bool Enabled { get; set; }

    /// <summary>Optional ordering hint for internal processing (None/Asc/Desc).</summary>
    public string? Sort { get; set; }

    /// <summary>Remove comments in formatted output.</summary>
    public bool RemoveComments { get; set; }

    /// <summary>Remove empty lines while preserving readability.</summary>
    public bool RemoveEmptyLines { get; set; }

    /// <summary>Remove all empty lines (more aggressive than RemoveEmptyLines).</summary>
    public bool RemoveAllEmptyLines { get; set; }

    /// <summary>Remove comments inside the param() block.</summary>
    public bool RemoveCommentsInParamBlock { get; set; }

    /// <summary>Remove comments immediately before the param() block.</summary>
    public bool RemoveCommentsBeforeParamBlock { get; set; }

    /// <summary>Formatter settings (IncludeRules + Rules).</summary>
    public FormatterSettingsOptions? FormatterSettings { get; set; }
}

/// <summary>
/// Formatter settings options (legacy).
/// </summary>
public sealed class FormatterSettingsOptions
{
    /// <summary>List of included rules.</summary>
    public string[]? IncludeRules { get; set; }

    /// <summary>Rule configuration map.</summary>
    public FormatterRulesOptions Rules { get; set; } = new();
}

/// <summary>
/// Rule configuration payload for the formatter (legacy: Rules.*).
/// </summary>
public sealed class FormatterRulesOptions
{
    /// <summary>PSPlaceOpenBrace rule settings.</summary>
    public BraceRuleOptions? PSPlaceOpenBrace { get; set; }

    /// <summary>PSPlaceCloseBrace rule settings.</summary>
    public CloseBraceRuleOptions? PSPlaceCloseBrace { get; set; }

    /// <summary>PSUseConsistentIndentation rule settings.</summary>
    public IndentationRuleOptions? PSUseConsistentIndentation { get; set; }

    /// <summary>PSUseConsistentWhitespace rule settings.</summary>
    public WhitespaceRuleOptions? PSUseConsistentWhitespace { get; set; }

    /// <summary>PSAlignAssignmentStatement rule settings.</summary>
    public AlignAssignmentRuleOptions? PSAlignAssignmentStatement { get; set; }

    /// <summary>PSUseCorrectCasing rule settings.</summary>
    public EnableOnlyRuleOptions? PSUseCorrectCasing { get; set; }
}

/// <summary>
/// Base rule options with only Enabled flag.
/// </summary>
public class EnableOnlyRuleOptions
{
    /// <summary>Enable the rule.</summary>
    public bool Enable { get; set; }
}

/// <summary>
/// Options for PSPlaceOpenBrace rule.
/// </summary>
public sealed class BraceRuleOptions : EnableOnlyRuleOptions
{
    /// <summary>Place opening brace on the same line.</summary>
    public bool OnSameLine { get; set; }

    /// <summary>Enforce a new line after the opening brace.</summary>
    public bool NewLineAfter { get; set; }

    /// <summary>Ignore single-line blocks.</summary>
    public bool IgnoreOneLineBlock { get; set; }
}

/// <summary>
/// Options for PSPlaceCloseBrace rule.
/// </summary>
public sealed class CloseBraceRuleOptions : EnableOnlyRuleOptions
{
    /// <summary>Enforce a new line after the closing brace.</summary>
    public bool NewLineAfter { get; set; }

    /// <summary>Ignore single-line blocks.</summary>
    public bool IgnoreOneLineBlock { get; set; }

    /// <summary>Do not allow an empty line before a closing brace.</summary>
    public bool NoEmptyLineBefore { get; set; }
}

/// <summary>
/// Options for PSUseConsistentIndentation rule.
/// </summary>
public sealed class IndentationRuleOptions : EnableOnlyRuleOptions
{
    /// <summary>Indentation kind (space/tab).</summary>
    public string? Kind { get; set; }

    /// <summary>Pipeline indentation mode.</summary>
    public string? PipelineIndentation { get; set; }

    /// <summary>Number of spaces used when Kind is space.</summary>
    public int IndentationSize { get; set; }
}

/// <summary>
/// Options for PSUseConsistentWhitespace rule.
/// </summary>
public sealed class WhitespaceRuleOptions : EnableOnlyRuleOptions
{
    /// <summary>Check inner brace spacing.</summary>
    public bool CheckInnerBrace { get; set; }

    /// <summary>Check open brace spacing.</summary>
    public bool CheckOpenBrace { get; set; }

    /// <summary>Check open parenthesis spacing.</summary>
    public bool CheckOpenParen { get; set; }

    /// <summary>Check operator spacing.</summary>
    public bool CheckOperator { get; set; }

    /// <summary>Check pipe spacing.</summary>
    public bool CheckPipe { get; set; }

    /// <summary>Check separator spacing.</summary>
    public bool CheckSeparator { get; set; }
}

/// <summary>
/// Options for PSAlignAssignmentStatement rule.
/// </summary>
public sealed class AlignAssignmentRuleOptions : EnableOnlyRuleOptions
{
    /// <summary>Check hashtables for alignment.</summary>
    public bool CheckHashtable { get; set; }
}
