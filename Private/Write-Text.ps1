function Write-Text {
    [CmdletBinding()]
    param(
        [string] $Text,
        [System.ConsoleColor] $Color = [System.ConsoleColor]::Cyan,
        [System.ConsoleColor] $ColorTime = [System.ConsoleColor]::Green,
        [switch] $Start,
        [switch] $End,
        [System.Diagnostics.Stopwatch] $Time
    )
    if (-not $Start -and -not $End) {
        Write-Host "$Text" -ForegroundColor $Color
    }
    if ($Start) {
        Write-Host "$Text" -NoNewline -ForegroundColor $Color
        $Time = [System.Diagnostics.Stopwatch]::StartNew()
    }
    if ($End) {
        $TimeToExecute = $Time.Elapsed.ToString()
        Write-Host " [Time: $TimeToExecute]" -ForegroundColor $ColorTime
        $Time.Stop()
    } else {
        if ($Time) {
            return $Time
        }
    }
}