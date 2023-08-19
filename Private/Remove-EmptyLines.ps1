function Remove-EmptyLines {
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
        [switch] $RemoveEmptyLines
    )

    if ($SourceFilePath) {
        $Fullpath = Resolve-Path -LiteralPath $SourceFilePath
        $Content = [IO.File]::ReadAllText($FullPath)
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