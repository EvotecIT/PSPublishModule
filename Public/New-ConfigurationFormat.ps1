function New-ConfigurationFormat {
    <#
    .SYNOPSIS
    Builds formatting options for code and manifest generation during the build.

    .DESCRIPTION
    Produces a configuration object that controls how script and manifest files are formatted
    during merge and in the default (non-merged) module. You can toggle specific PSScriptAnalyzer
    rules, whitespace/indentation behavior, comment removal, and choose PSD1 output style.

    .PARAMETER ApplyTo
    One or more targets to apply formatting to: OnMergePSM1, OnMergePSD1, DefaultPSM1, DefaultPSD1.

    .PARAMETER EnableFormatting
    When set, enables formatting for the chosen ApplyTo targets even if no specific rule switches are provided.

    .PARAMETER Sort
    Optional ordering hint for internal processing. Accepts None, Asc, or Desc.

    .PARAMETER RemoveComments
    Remove comments in the formatted output.

    .PARAMETER RemoveEmptyLines
    Remove empty lines while preserving readability.

    .PARAMETER RemoveAllEmptyLines
    Remove all empty lines (more aggressive than RemoveEmptyLines).

    .PARAMETER RemoveCommentsInParamBlock
    Remove comments within the param() block.

    .PARAMETER RemoveCommentsBeforeParamBlock
    Remove comments that appear immediately before the param() block.

    .PARAMETER PlaceOpenBraceEnable
    Enable PSPlaceOpenBrace rule and configure its behavior.

    .PARAMETER PlaceOpenBraceOnSameLine
    For PSPlaceOpenBrace: place opening brace on the same line.

    .PARAMETER PlaceOpenBraceNewLineAfter
    For PSPlaceOpenBrace: enforce a new line after the opening brace.

    .PARAMETER PlaceOpenBraceIgnoreOneLineBlock
    For PSPlaceOpenBrace: ignore single-line blocks.

    .PARAMETER PlaceCloseBraceEnable
    Enable PSPlaceCloseBrace rule and configure its behavior.

    .PARAMETER PlaceCloseBraceNewLineAfter
    For PSPlaceCloseBrace: enforce a new line after the closing brace.

    .PARAMETER PlaceCloseBraceIgnoreOneLineBlock
    For PSPlaceCloseBrace: ignore single-line blocks.

    .PARAMETER PlaceCloseBraceNoEmptyLineBefore
    For PSPlaceCloseBrace: do not allow an empty line before a closing brace.

    .PARAMETER UseConsistentIndentationEnable
    Enable PSUseConsistentIndentation rule and configure its behavior.

    .PARAMETER UseConsistentIndentationKind
    Indentation style: 'space' or 'tab'.

    .PARAMETER UseConsistentIndentationPipelineIndentation
    Pipeline indentation mode: IncreaseIndentationAfterEveryPipeline or NoIndentation.

    .PARAMETER UseConsistentIndentationIndentationSize
    Number of spaces for indentation when Kind is 'space'.

    .PARAMETER UseConsistentWhitespaceEnable
    Enable PSUseConsistentWhitespace rule and configure which elements to check.

    .PARAMETER UseConsistentWhitespaceCheckInnerBrace
    For PSUseConsistentWhitespace: check inner brace spacing.

    .PARAMETER UseConsistentWhitespaceCheckOpenBrace
    For PSUseConsistentWhitespace: check open brace spacing.

    .PARAMETER UseConsistentWhitespaceCheckOpenParen
    For PSUseConsistentWhitespace: check open parenthesis spacing.

    .PARAMETER UseConsistentWhitespaceCheckOperator
    For PSUseConsistentWhitespace: check operator spacing.

    .PARAMETER UseConsistentWhitespaceCheckPipe
    For PSUseConsistentWhitespace: check pipeline operator spacing.

    .PARAMETER UseConsistentWhitespaceCheckSeparator
    For PSUseConsistentWhitespace: check separator (comma) spacing.

    .PARAMETER AlignAssignmentStatementEnable
    Enable PSAlignAssignmentStatement rule and optionally check hashtable alignment.

    .PARAMETER AlignAssignmentStatementCheckHashtable
    For PSAlignAssignmentStatement: align hashtable assignments.

    .PARAMETER UseCorrectCasingEnable
    Enable PSUseCorrectCasing rule.

    .PARAMETER PSD1Style
    Style for generated manifests (PSD1) for the selected ApplyTo targets. 'Minimal' or 'Native'.

    .EXAMPLE
    New-ConfigurationFormat -ApplyTo 'OnMergePSD1','DefaultPSD1' -PSD1Style 'Minimal'
    Minimizes PSD1 output during merge and default builds.

    .EXAMPLE
    New-ConfigurationFormat -ApplyTo 'OnMergePSM1' -EnableFormatting -UseConsistentIndentationEnable -UseConsistentIndentationKind space -UseConsistentIndentationIndentationSize 4
    Enables indentation and whitespace rules for merged PSM1.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [validateSet(
            'OnMergePSM1', 'OnMergePSD1',
            'DefaultPSM1', 'DefaultPSD1'
            #"DefaultPublic", 'DefaultPrivate', 'DefaultOther'
        )][string[]]$ApplyTo,

        [switch] $EnableFormatting,

        [validateSet('None', 'Asc', 'Desc')][string] $Sort,

        [switch] $RemoveComments,
        [switch] $RemoveEmptyLines,
        [switch] $RemoveAllEmptyLines,
        [switch] $RemoveCommentsInParamBlock,
        [switch] $RemoveCommentsBeforeParamBlock,

        [switch] $PlaceOpenBraceEnable,
        [switch] $PlaceOpenBraceOnSameLine,
        [switch] $PlaceOpenBraceNewLineAfter,
        [switch] $PlaceOpenBraceIgnoreOneLineBlock,

        [switch] $PlaceCloseBraceEnable,
        [switch] $PlaceCloseBraceNewLineAfter,
        [switch] $PlaceCloseBraceIgnoreOneLineBlock,
        [switch] $PlaceCloseBraceNoEmptyLineBefore,

        [switch] $UseConsistentIndentationEnable,
        [ValidateSet('space', 'tab')][string] $UseConsistentIndentationKind,
        [ValidateSet('IncreaseIndentationAfterEveryPipeline', 'NoIndentation')][string] $UseConsistentIndentationPipelineIndentation,
        [int] $UseConsistentIndentationIndentationSize,

        [switch] $UseConsistentWhitespaceEnable,
        [switch] $UseConsistentWhitespaceCheckInnerBrace,
        [switch] $UseConsistentWhitespaceCheckOpenBrace,
        [switch] $UseConsistentWhitespaceCheckOpenParen,
        [switch] $UseConsistentWhitespaceCheckOperator,
        [switch] $UseConsistentWhitespaceCheckPipe,
        [switch] $UseConsistentWhitespaceCheckSeparator,

        [switch] $AlignAssignmentStatementEnable,
        [switch] $AlignAssignmentStatementCheckHashtable,

        [switch] $UseCorrectCasingEnable,

        [ValidateSet('Minimal', 'Native')][string] $PSD1Style
    )
    $SettingsCount = 0

    $Options = [ordered] @{
        Merge    = [ordered] @{
            #Sort = $Sort
        }
        Standard = [ordered] @{
            #Sort = $Sort
        }
    }

    foreach ($Apply in $ApplyTo) {
        $Formatting = [ordered] @{}
        if ($PSBoundParameters.ContainsKey('RemoveComments')) {
            $Formatting.RemoveComments = $RemoveComments.IsPresent
        }
        if ($PSBoundParameters.ContainsKey('RemoveEmptyLines')) {
            $Formatting.RemoveEmptyLines = $RemoveEmptyLines.IsPresent
        }
        if ($PSBoundParameters.ContainsKey('RemoveAllEmptyLines')) {
            $Formatting.RemoveAllEmptyLines = $RemoveAllEmptyLines.IsPresent
        }
        if ($PSBoundParameters.ContainsKey('RemoveCommentsInParamBlock')) {
            $Formatting.RemoveCommentsInParamBlock = $RemoveCommentsInParamBlock.IsPresent
        }
        if ($PSBoundParameters.ContainsKey('RemoveCommentsBeforeParamBlock')) {
            $Formatting.RemoveCommentsBeforeParamBlock = $RemoveCommentsBeforeParamBlock.IsPresent
        }

        $Formatting.FormatterSettings = [ordered] @{
            IncludeRules = @(
                if ($PlaceOpenBraceEnable) { 'PSPlaceOpenBrace' }
                if ($PlaceCloseBraceEnable) { 'PSPlaceCloseBrace' }
                if ($UseConsistentIndentationEnable) { 'PSUseConsistentIndentation' }
                if ($UseConsistentWhitespaceEnable) { 'PSUseConsistentWhitespace' }
                if ($AlignAssignmentStatementEnable) { 'PSAlignAssignmentStatement' }
                if ($UseCorrectCasingEnable) { 'PSUseCorrectCasing' }
            )
            Rules        = [ordered] @{}
        }
        if ($PlaceOpenBraceEnable) {
            $Formatting.FormatterSettings.Rules.PSPlaceOpenBrace = [ordered] @{
                Enable             = $true
                OnSameLine         = $PlaceOpenBraceOnSameLine.IsPresent
                NewLineAfter       = $PlaceOpenBraceNewLineAfter.IsPresent
                IgnoreOneLineBlock = $PlaceOpenBraceIgnoreOneLineBlock.IsPresent
            }
        }
        if ($PlaceCloseBraceEnable) {
            $Formatting.FormatterSettings.Rules.PSPlaceCloseBrace = [ordered] @{
                Enable             = $true
                NewLineAfter       = $PlaceCloseBraceNewLineAfter.IsPresent
                IgnoreOneLineBlock = $PlaceCloseBraceIgnoreOneLineBlock.IsPresent
                NoEmptyLineBefore  = $PlaceCloseBraceNoEmptyLineBefore.IsPresent
            }
        }
        if ($UseConsistentIndentationEnable) {
            $Formatting.FormatterSettings.Rules.PSUseConsistentIndentation = [ordered] @{
                Enable              = $true
                Kind                = $UseConsistentIndentationKind
                PipelineIndentation = $UseConsistentIndentationPipelineIndentation
                IndentationSize     = $UseConsistentIndentationIndentationSize
            }
        }
        if ($UseConsistentWhitespaceEnable) {
            $Formatting.FormatterSettings.Rules.PSUseConsistentWhitespace = [ordered] @{
                Enable          = $true
                CheckInnerBrace = $UseConsistentWhitespaceCheckInnerBrace.IsPresent
                CheckOpenBrace  = $UseConsistentWhitespaceCheckOpenBrace.IsPresent
                CheckOpenParen  = $UseConsistentWhitespaceCheckOpenParen.IsPresent
                CheckOperator   = $UseConsistentWhitespaceCheckOperator.IsPresent
                CheckPipe       = $UseConsistentWhitespaceCheckPipe.IsPresent
                CheckSeparator  = $UseConsistentWhitespaceCheckSeparator.IsPresent
            }
        }
        if ($AlignAssignmentStatementEnable) {
            $Formatting.FormatterSettings.Rules.PSAlignAssignmentStatement = [ordered] @{
                Enable         = $true
                CheckHashtable = $AlignAssignmentStatementCheckHashtable.IsPresent
            }
        }
        if ($UseCorrectCasingEnable) {
            $Formatting.FormatterSettings.Rules.PSUseCorrectCasing = [ordered] @{
                Enable = $true
            }
        }
        Remove-EmptyValue -Hashtable $Formatting.FormatterSettings -Recursive
        if ($Formatting.FormatterSettings.Keys.Count -eq 0) {
            $null = $Formatting.Remove('FormatterSettings')
        }

        if ($Formatting.Count -gt 0 -or $EnableFormatting) {
            $SettingsCount++
            $Formatting.Enabled = $true

            if ($Apply -eq 'OnMergePSM1') {
                $Options.Merge.FormatCodePSM1 = $Formatting
            } elseif ($Apply -eq 'OnMergePSD1') {
                $Options.Merge.FormatCodePSD1 = $Formatting
            } elseif ($Apply -eq 'DefaultPSM1') {
                $Options.Standard.FormatCodePSM1 = $Formatting
            } elseif ($Apply -eq 'DefaultPSD1') {
                $Options.Standard.FormatCodePSD1 = $Formatting
            } elseif ($Apply -eq 'DefaultPublic') {
                $Options.Standard.FormatCodePublic = $Formatting
            } elseif ($Apply -eq 'DefaultPrivate') {
                $Options.Standard.FormatCodePrivate = $Formatting
            } elseif ($Apply -eq 'DefaultOther') {
                $Options.Standard.FormatCodeOther = $Formatting
            } else {
                throw "Unknown ApplyTo: $Apply"
            }
        }
        if ($PSD1Style) {
            if ($Apply -eq 'OnMergePSD1') {
                $SettingsCount++
                $Options['Merge']['Style'] = [ordered] @{}
                $Options['Merge']['Style']['PSD1'] = $PSD1Style
            } elseif ($Apply -eq 'DefaultPSD1') {
                $SettingsCount++
                $Options['Standard']['Style'] = [ordered] @{}
                $Options['Standard']['Style']['PSD1'] = $PSD1Style
            }
        }
    }

    # Set formatting options if present
    if ($SettingsCount -gt 0) {
        $Output = [ordered] @{
            Type    = 'Formatting'
            Options = $Options
        }
        $Output
    }
}

# $Config = New-ConfigurationFormat -ApplyTo OnMergePSD1, DefaultPSD1 -PSD1Style Minimal
# $Config.Options
