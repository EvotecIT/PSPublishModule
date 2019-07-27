function Write-TextWithTime {
    [CmdletBinding()]
    param(
        [ScriptBlock] $Content,
        [string] $Text,
        [ValidateSet('OneLiner', 'Array')][string] $Option = 'OneLiner',
        [switch] $Continue,
        [System.ConsoleColor] $Color = [System.ConsoleColor]::Cyan,
        [System.ConsoleColor] $ColorTime = [System.ConsoleColor]::Green
    )
    Write-Host "[$Text]" -NoNewline -ForegroundColor $Color
    $Time = [System.Diagnostics.Stopwatch]::StartNew()
    if ($null -ne $Content) {
        & $Content
    }
    if ($Option -eq 'Array') {
        $TimeToExecute = "$($Time.Elapsed.Days) days", "$($Time.Elapsed.Hours) hours", "$($Time.Elapsed.Minutes) minutes", "$($Time.Elapsed.Seconds) seconds", "$($Time.Elapsed.Milliseconds) milliseconds"
    } else {
        $TimeToExecute = "$($Time.Elapsed.Days) days, $($Time.Elapsed.Hours) hours, $($Time.Elapsed.Minutes) minutes, $($Time.Elapsed.Seconds) seconds, $($Time.Elapsed.Milliseconds) milliseconds"
    }
    Write-Host " [Time: $TimeToExecute]" -ForegroundColor $ColorTime
    if (-not $Continue) {
        $Time.Stop()
    }
}