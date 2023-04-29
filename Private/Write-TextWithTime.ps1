function Write-TextWithTime {
    [CmdletBinding()]
    param(
        [ScriptBlock] $Content,
        [ValidateSet('Plus', 'Minus', 'Information')][string] $PreAppend,
        [string] $Text,
        [switch] $Continue,
        [System.ConsoleColor] $Color = [System.ConsoleColor]::Cyan,
        [System.ConsoleColor] $ColorTime = [System.ConsoleColor]::Green,
        [System.ConsoleColor] $ColorError = [System.ConsoleColor]::Red,
        [string] $SpacesBefore
    )
    if ($PreAppend) {
        if ($PreAppend -eq "Information") {
            $TextBefore = "$SpacesBefore[i] "
            $ColorBefore = [System.ConsoleColor]::Yellow
        } elseif ($PreAppend -eq 'Minus') {
            $TextBefore = "$SpacesBefore[-] "
            $ColorBefore = [System.ConsoleColor]::Red
        } elseif ($PreAppend -eq 'Plus') {
            $TextBefore = "$SpacesBefore[+] "
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
            $InputData = & $Content
            if ($InputData -contains $false) {
                $ErrorMessage = "Failure in scriptblock. Check output."
            } else {
                $InputData
            }
        } catch {
            $ErrorMessage = $_.Exception.Message
        }
    }
    $TimeToExecute = $Time.Elapsed.ToString()
    if ($ErrorMessage) {
        Write-Host -Object "$SpacesBefore[e] $Text [Error: $ErrorMessage]" -ForegroundColor $ColorError
        if ($PreAppend) {
            Write-Host -Object "$($TextBefore)" -NoNewline -ForegroundColor $ColorError
        }
        Write-Host -Object "$Text [Time: $TimeToExecute]" -ForegroundColor $ColorError
        $Time.Stop()
        return $false
        break
    } else {
        if ($PreAppend) {
            Write-Host -Object "$($TextBefore)" -NoNewline -ForegroundColor $ColorBefore
        }
        Write-Host -Object "$Text [Time: $TimeToExecute]" -ForegroundColor $ColorTime
    }
    if (-not $Continue) {
        $Time.Stop()
    }
}