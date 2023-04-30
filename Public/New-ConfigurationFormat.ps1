function New-ConfigurationFormat {
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