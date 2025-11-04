function Test-ReparsePoint {
    [CmdletBinding()]
    param (
        [string]$path
    )
    $file = Get-Item $path -Force -ea SilentlyContinue
    return [bool]($file.Attributes -band [IO.FileAttributes]::ReparsePoint)
}