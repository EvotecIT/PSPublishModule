function New-ConfigurationFormat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [validateSet(
            'OnMergePSM1', 'OnMergePSD1',
            'DefaultPSM1', 'DefaultPSD1'
            #"DefaultPublic", 'DefaultPrivate', 'DefaultOther'
        )][string[]]$ApplyTo,

        [validateSet('None', 'Asc', 'Desc')][string] $Sort,
        [switch] $RemoveComments,

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

        [switch] $UseCorrectCasingEnable
    )


    if (-not $Sort) {
        $Sort = 'None'
    }

    $Options = [ordered] @{
        Merge    = [ordered] @{
            Sort = $Sort
        }
        Standard = [ordered] @{
            Sort = $Sort
        }
    }

    foreach ($Apply in $ApplyTo) {
        if ($Apply -eq 'OnMergePSM1') {
            $Formatting = $Options.Merge.FormatCodePSM1 = [ordered] @{}
        } elseif ($Apply -eq 'OnMergePSD1') {
            $Formatting = $Options.Merge.FormatCodePSD1 = [ordered] @{}
        } elseif ($Apply -eq 'DefaultPSM1') {
            $Formatting = $Options.Standard.FormatCodePSM1 = [ordered] @{}
        } elseif ($Apply -eq 'DefaultPSD1') {
            $Formatting = $Options.Standard.FormatCodePSD1 = [ordered] @{}
        } elseif ($Apply -eq 'DefaultPublic') {
            $Formatting = $Options.Standard.FormatCodePublic = [ordered] @{}
        } elseif ($Apply -eq 'DefaultPrivate') {
            $Formatting = $Options.Standard.FormatCodePrivate = [ordered] @{}
        } elseif ($Apply -eq 'DefaultOther') {
            $Formatting = $Options.Standard.FormatCodeOther = [ordered] @{}
        } else {
            throw "Unknown ApplyTo: $Apply"
        }
        $Formatting.Enabled = $true
        $Formatting.RemoveComments = $RemoveComments

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
                OnSameLine         = $PlaceOpenBraceOnSameLine
                NewLineAfter       = $PlaceOpenBraceNewLineAfter
                IgnoreOneLineBlock = $PlaceOpenBraceIgnoreOneLineBlock
            }
        }
        if ($PlaceCloseBraceEnable) {
            $Formatting.FormatterSettings.Rules.PSPlaceCloseBrace = [ordered] @{
                Enable             = $true
                NewLineAfter       = $PlaceCloseBraceNewLineAfter
                IgnoreOneLineBlock = $PlaceCloseBraceIgnoreOneLineBlock
                NoEmptyLineBefore  = $PlaceCloseBraceNoEmptyLineBefore
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
                CheckInnerBrace = $UseConsistentWhitespaceCheckInnerBrace
                CheckOpenBrace  = $UseConsistentWhitespaceCheckOpenBrace
                CheckOpenParen  = $UseConsistentWhitespaceCheckOpenParen
                CheckOperator   = $UseConsistentWhitespaceCheckOperator
                CheckPipe       = $UseConsistentWhitespaceCheckPipe
                CheckSeparator  = $UseConsistentWhitespaceCheckSeparator
            }
        }
        if ($AlignAssignmentStatementEnable) {
            $Formatting.FormatterSettings.Rules.PSAlignAssignmentStatement = [ordered] @{
                Enable         = $true
                CheckHashtable = $AlignAssignmentStatementCheckHashtable
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
    }
    $Output = @{
        Type    = 'Formatting'
        Options = $Options
    }
    $Output
}