function Remove-Comments {
    <#
    .SYNOPSIS
    Remove comments from PowerShell file

    .DESCRIPTION
    Remove comments from PowerShell file and optionally remove empty lines
    By default comments in param block are not removed
    By default comments before param block are not removed

    .PARAMETER SourceFilePath
    File path to the source file

    .PARAMETER Content
    Content of the file

    .PARAMETER DestinationFilePath
    File path to the destination file. If not provided, the content will be returned

    .PARAMETER RemoveEmptyLines
    Remove empty lines if more than one empty line is found

    .PARAMETER RemoveAllEmptyLines
    Remove all empty lines from the content

    .PARAMETER RemoveCommentsInParamBlock
    Remove comments in param block. By default comments in param block are not removed

    .PARAMETER RemoveCommentsBeforeParamBlock
    Remove comments before param block. By default comments before param block are not removed

    .EXAMPLE
    Remove-Comments -SourceFilePath 'C:\Support\GitHub\PSPublishModule\Examples\TestScript.ps1' -DestinationFilePath 'C:\Support\GitHub\PSPublishModule\Examples\TestScript1.ps1' -RemoveAllEmptyLines -RemoveCommentsInParamBlock -RemoveCommentsBeforeParamBlock

    .NOTES
    Most of the work done by Chris Dent, with improvements by Przemyslaw Klys

    #>
    [CmdletBinding(DefaultParameterSetName = 'FilePath')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'FilePath')]
        [alias('FilePath', 'Path', 'LiteralPath')][string] $SourceFilePath,

        [Parameter(Mandatory, ParameterSetName = 'Content')][string] $Content,

        [Parameter(ParameterSetName = 'Content')]
        [Parameter(ParameterSetName = 'FilePath')]
        [alias('Destination')][string] $DestinationFilePath,

        [Parameter(ParameterSetName = 'Content')]
        [Parameter(ParameterSetName = 'FilePath')]
        [switch] $RemoveAllEmptyLines,

        [Parameter(ParameterSetName = 'Content')]
        [Parameter(ParameterSetName = 'FilePath')]
        [switch] $RemoveEmptyLines,

        [Parameter(ParameterSetName = 'Content')]
        [Parameter(ParameterSetName = 'FilePath')]
        [switch] $RemoveCommentsInParamBlock,

        [Parameter(ParameterSetName = 'Content')]
        [Parameter(ParameterSetName = 'FilePath')]
        [switch] $RemoveCommentsBeforeParamBlock
    )
    if ($SourceFilePath) {
        $Fullpath = Resolve-Path -LiteralPath $SourceFilePath
        $Content = [IO.File]::ReadAllText($FullPath)
    }

    $Tokens = $Errors = @()
    $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Content, [ref]$Tokens, [ref]$Errors)
    #$functionDefinition = $ast.Find({ $args[0] -is [FunctionDefinitionAst] }, $false)
    $groupedTokens = $Tokens | Group-Object { $_.Extent.StartLineNumber }
    $DoNotRemove = $false
    $DoNotRemoveCommentParam = $false
    $CountParams = 0
    $ParamFound = $false
    $toRemove = foreach ($line in $groupedTokens) {
        if ($Ast.Body.ParamBlock.Extent.StartLineNumber -gt $line.Name) {
            continue
        }
        $tokens = $line.Group
        for ($i = 0; $i -lt $line.Count; $i++) {
            $token = $tokens[$i]
            if ($token.Extent.StartOffset -lt $Ast.Body.ParamBlock.Extent.StartOffset) {
                continue
            }

            # Lets find comments between function and param block and not remove them
            if ($token.Extent.Text -eq 'function') {
                if (-not $RemoveCommentsBeforeParamBlock) {
                    $DoNotRemove = $true
                }
                continue
            }
            if ($token.Extent.Text -eq 'param') {
                $ParamFound = $true
                $DoNotRemove = $false
            }
            if ($DoNotRemove) {
                continue
            }
            # lets find comments between param block and end of param block
            if ($token.Extent.Text -eq 'param') {
                if (-not $RemoveCommentsInParamBlock) {
                    $DoNotRemoveCommentParam = $true
                }
                continue
            }
            if ($ParamFound -and ($token.Extent.Text -eq '(' -or $token.Extent.Text -eq '@(')) {
                $CountParams += 1
            } elseif ($ParamFound -and $token.Extent.Text -eq ')') {
                $CountParams -= 1
            }
            if ($ParamFound -and $token.Extent.Text -eq ')') {
                if ($CountParams -eq 0) {
                    $DoNotRemoveCommentParam = $false
                    $ParamFound = $false
                }
            }
            if ($DoNotRemoveCommentParam) {
                continue
            }

            # if token not comment we leave it as is
            if ($token.Kind -ne 'Comment') {
                continue
            }
            $token
        }
    }
    $toRemove = $toRemove | Sort-Object { $_.Extent.StartOffset } -Descending
    foreach ($token in $toRemove) {
        $StartIndex = $token.Extent.StartOffset
        $HowManyChars = $token.Extent.EndOffset - $token.Extent.StartOffset
        $content = $content.Remove($StartIndex, $HowManyChars)
    }
    if ($RemoveEmptyLines) {
        # Remove empty lines if more than one empty line is found. If it's just one line, leave it as is
        $Content = $Content -replace '(?m)^\s*$', ''
    }
    if ($RemoveAllEmptyLines) {
        # Remove all empty lines from the content
        $Content = $Content -replace '(?m)^\s*$(\r?\n)?', ''
    }
    if ($Content) {
        $Content = $Content.Trim()
    }
    if ($DestinationFilePath) {
        $Content | Set-Content -Path $DestinationFilePath -Encoding utf8
    } else {
        $Content
    }
}