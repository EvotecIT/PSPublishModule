[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Path,
    [ValidateRange(0, 1000000)]
    [int] $MinimumLength = 1
)

if (-not [System.IO.File]::Exists($Path)) {
    throw [System.IO.FileNotFoundException]::new("Input file does not exist: $Path")
}

[string[]] $lines = [System.IO.File]::ReadAllLines($Path)
[int] $matching = 0
[long] $characters = 0
foreach ($line in $lines) {
    if ($line.Length -ge $MinimumLength) {
        $matching++
        $characters += $line.Length
    }
}
[int] $lineCount = $lines.Length
return "lines=$lineCount; matching=$matching; characters=$characters"
