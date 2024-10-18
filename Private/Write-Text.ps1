function Write-Text {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)][string] $Text,
        [System.ConsoleColor] $Color,
        [System.ConsoleColor] $ColorBefore,
        [System.ConsoleColor] $ColorTime,
        [switch] $Start,
        [switch] $End,
        [System.Diagnostics.Stopwatch] $Time,
        [ValidateSet('Plus', 'Minus', 'Information', 'Addition', 'Error')][string] $PreAppend,
        [string] $SpacesBefore
    )
    if ($PreAppend) {
        if ($PreAppend -eq "Information") {
            $TextBefore = "$SpacesBefore[i] "
            if (-not $ColorBefore) {
                $ColorBefore = [System.ConsoleColor]::Yellow
            }
        } elseif ($PreAppend -eq 'Minus') {
            $TextBefore = "$SpacesBefore[-] "
            if (-not $ColorBefore) {
                $ColorBefore = [System.ConsoleColor]::Red
            }
        } elseif ($PreAppend -eq 'Plus') {
            $TextBefore = "$SpacesBefore[+] "
            if (-not $ColorBefore) {
                $ColorBefore = [System.ConsoleColor]::Cyan
            }
        } elseif ($PreAppend -eq 'Addition') {
            $TextBefore = "$SpacesBefore[>] "
            if (-not $ColorBefore) {
                $ColorBefore = [System.ConsoleColor]::Yellow
            }
        } elseif ($PreAppend -eq 'Error') {
            $TextBefore = "$SpacesBefore[e] "
            if (-not $ColorBefore) {
                $ColorBefore = [System.ConsoleColor]::Red
            }
            if (-not $Color) {
                $Color = [System.ConsoleColor]::Red
            }
        }
        Write-Host -Object "$TextBefore" -NoNewline -ForegroundColor $ColorBefore
    }
    if (-not $Color) {
        $Color = [System.ConsoleColor]::Cyan
    }
    if (-not $ColorTime) {
        $ColorTime = [System.ConsoleColor]::Green
    }
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