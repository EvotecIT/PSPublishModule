param(
    [Parameter(Mandatory)] [string] $ToolPath,
    [Parameter(Mandatory)] [string] $ArgumentListBase64
)

$ErrorActionPreference = 'Stop'

try {
    $argumentJson = [System.Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String($ArgumentListBase64))
    $toolArguments = [string[]] @($argumentJson | ConvertFrom-Json -Depth 10)
    & $ToolPath @toolArguments
    $succeeded = $?
    $nativeExitCode = $LASTEXITCODE
    if ($null -ne $nativeExitCode) { exit [int] $nativeExitCode }
    if (-not $succeeded) { exit 1 }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
