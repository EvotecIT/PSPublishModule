using System;

namespace PowerForge;

internal sealed class FormatConfigurationRequest
{
    public string[] ApplyTo { get; set; } = Array.Empty<string>();
    public bool EnableFormatting { get; set; }
    public string? Sort { get; set; }
    public bool RemoveCommentsSpecified { get; set; }
    public bool RemoveComments { get; set; }
    public bool RemoveEmptyLinesSpecified { get; set; }
    public bool RemoveEmptyLines { get; set; }
    public bool RemoveAllEmptyLinesSpecified { get; set; }
    public bool RemoveAllEmptyLines { get; set; }
    public bool RemoveCommentsInParamBlockSpecified { get; set; }
    public bool RemoveCommentsInParamBlock { get; set; }
    public bool RemoveCommentsBeforeParamBlockSpecified { get; set; }
    public bool RemoveCommentsBeforeParamBlock { get; set; }
    public bool UpdateProjectRoot { get; set; }
    public bool PlaceOpenBraceEnable { get; set; }
    public bool PlaceOpenBraceOnSameLine { get; set; }
    public bool PlaceOpenBraceNewLineAfter { get; set; }
    public bool PlaceOpenBraceIgnoreOneLineBlock { get; set; }
    public bool PlaceCloseBraceEnable { get; set; }
    public bool PlaceCloseBraceNewLineAfter { get; set; }
    public bool PlaceCloseBraceIgnoreOneLineBlock { get; set; }
    public bool PlaceCloseBraceNoEmptyLineBefore { get; set; }
    public bool UseConsistentIndentationEnable { get; set; }
    public string? UseConsistentIndentationKind { get; set; }
    public string? UseConsistentIndentationPipelineIndentation { get; set; }
    public int UseConsistentIndentationIndentationSize { get; set; }
    public bool UseConsistentWhitespaceEnable { get; set; }
    public bool UseConsistentWhitespaceCheckInnerBrace { get; set; }
    public bool UseConsistentWhitespaceCheckOpenBrace { get; set; }
    public bool UseConsistentWhitespaceCheckOpenParen { get; set; }
    public bool UseConsistentWhitespaceCheckOperator { get; set; }
    public bool UseConsistentWhitespaceCheckPipe { get; set; }
    public bool UseConsistentWhitespaceCheckSeparator { get; set; }
    public bool AlignAssignmentStatementEnable { get; set; }
    public bool AlignAssignmentStatementCheckHashtable { get; set; }
    public bool UseCorrectCasingEnable { get; set; }
    public string? PSD1Style { get; set; }
}
