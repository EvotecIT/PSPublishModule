function Write-TextWithTime {
    [CmdletBinding()]
    param(
        [ScriptBlock] $Content,
        [ValidateSet('Plus', 'Minus', 'Information')][string] $PreAppend,
        [string] $Text,
        [switch] $Continue,
        [System.ConsoleColor] $Color = [System.ConsoleColor]::Cyan,
        [System.ConsoleColor] $ColorTime = [System.ConsoleColor]::Green,
        [System.ConsoleColor] $ColorError = [System.ConsoleColor]::Red
    )
    if ($PreAppend) {
        if ($PreAppend -eq "Information") {
            $TextBefore = '[ℹ]'
            $ColorBefore = [System.ConsoleColor]::Yellow
        } elseif ($PreAppend -eq 'Minus') {
            $TextBefore = '[-]'
            $ColorBefore = [System.ConsoleColor]::Red
        } elseif ($PreAppend -eq 'Plus') {
            $TextBefore = '[+] '
            $ColorBefore = [System.ConsoleColor]::Cyan
        }
        Write-Host "$TextBefore" -NoNewline -ForegroundColor $ColorBefore
        Write-Host "$Text" -ForegroundColor $Color
    } else {
        Write-Host "$Text" -ForegroundColor $Color
    }
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
        Write-Host "[-] $Text [Error: $ErrorMessage]" -ForegroundColor $ColorError
        $Time.Stop()
        break
    } else {
        if ($PreAppend) {
            Write-Host "$TextBefore" -NoNewline -ForegroundColor $ColorBefore
        }
        Write-Host "$Text [Time: $TimeToExecute]" -ForegroundColor $ColorTime
    }
    if (-not $Continue) {
        $Time.Stop()
    }
}