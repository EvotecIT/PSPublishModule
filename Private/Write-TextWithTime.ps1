function Write-TextWithTime {
    [CmdletBinding()]
    param(
        [ScriptBlock] $Content,
        [string] $Text,
        [switch] $Continue,
        [System.ConsoleColor] $Color = [System.ConsoleColor]::Cyan,
        [System.ConsoleColor] $ColorTime = [System.ConsoleColor]::Green,
        [System.ConsoleColor] $ColorError = [System.ConsoleColor]::Red
    )
    Write-Host "$Text" -NoNewline -ForegroundColor $Color
    $Time = [System.Diagnostics.Stopwatch]::StartNew()
    if ($null -ne $Content) {
        try {
            & $Content
        } catch {
            $ErrorMessage = $_.Exception.Message
        }
    }
    $TimeToExecute = $Time.Elapsed.ToString()
    if ($ErrorMessage) {
        Write-Host " [Time: $TimeToExecute]" -ForegroundColor $ColorError
        Write-Host "[-] [Error: $ErrorMessage]" -ForegroundColor $ColorError
        $Time.Stop()
        Exit
    } else {
        Write-Host " [Time: $TimeToExecute]" -ForegroundColor $ColorTime
    }
    if (-not $Continue) {
        $Time.Stop()
    }
}