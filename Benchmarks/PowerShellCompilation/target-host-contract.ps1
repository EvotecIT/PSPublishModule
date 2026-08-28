param(
    [Parameter(Mandatory)] [string] $Text,
    [Parameter(Mandatory)] [string] $ResourcePath,
    [int] $Iterations = 5
)

[string] $resource = [System.IO.File]::ReadAllText($ResourcePath)
[System.Threading.Thread]::Sleep($Iterations)
[long] $checksum = 0
for ([int] $index = 0; $index -lt $Iterations; $index++) {
    $checksum += $index
}
[string] $checksumText = $checksum.ToString()
return "$Text|$resource|$checksumText"
