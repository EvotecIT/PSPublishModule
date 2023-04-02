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
            $TextBefore = '[i] '
            $ColorBefore = [System.ConsoleColor]::Yellow
        } elseif ($PreAppend -eq 'Minus') {
            $TextBefore = '[-] '
            $ColorBefore = [System.ConsoleColor]::Red
        } elseif ($PreAppend -eq 'Plus') {
            $TextBefore = '[+] '
            $ColorBefore = [System.ConsoleColor]::Cyan
        }
        Write-Host -Object "$TextBefore" -NoNewline -ForegroundColor $ColorBefore
        Write-Host -Object "$Text" -ForegroundColor $Color
    } else {
        Write-Host -Object "$Text" -ForegroundColor $Color
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
        #Write-Host -Object " [Time: $TimeToExecute]" -ForegroundColor $ColorError
        Write-Host -Object "[e] $Text [Error: $ErrorMessage]" -ForegroundColor $ColorError
        if ($PreAppend) {
            Write-Host -Object "[i] " -NoNewline -ForegroundColor $ColorError
        }
        Write-Host -Object "$Text [Time: $TimeToExecute]" -ForegroundColor $ColorError
        $Time.Stop()
        break
    } else {
        if ($PreAppend) {
            Write-Host -Object "$TextBefore" -NoNewline -ForegroundColor $ColorBefore
        }
        Write-Host -Object "$Text [Time: $TimeToExecute]" -ForegroundColor $ColorTime
    }
    if (-not $Continue) {
        $Time.Stop()
    }
}