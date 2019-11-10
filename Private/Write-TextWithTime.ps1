function Write-TextWithTime {
    [CmdletBinding()]
    param(
        [ScriptBlock] $Content,
        [string] $Text,
        [switch] $Continue,
        [System.ConsoleColor] $Color = [System.ConsoleColor]::Cyan,
        [System.ConsoleColor] $ColorTime = [System.ConsoleColor]::Green
    )
    Write-Host "$Text" -NoNewline -ForegroundColor $Color
    $Time = [System.Diagnostics.Stopwatch]::StartNew()
    if ($null -ne $Content) {
        & $Content
    }
    $TimeToExecute = $Time.Elapsed.ToString()
    Write-Host " [Time: $TimeToExecute]" -ForegroundColor $ColorTime
    if (-not $Continue) {
        $Time.Stop()
    }
}