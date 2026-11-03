<#
.SYNOPSIS
Applies explicitly configured terminal and locale values to subsequent test steps.
.DESCRIPTION
Appends supplied values to the current GitHub job environment file. Omitted values
keep the runner defaults. The current process environment is unchanged.
.PARAMETER Term
Optional TERM value from the test matrix.
.PARAMETER Lang
Optional LANG value from the test matrix.
.EXAMPLE
./Set-ModuleTestEnvironment.ps1 -Term xterm-256color -Lang C.UTF-8
#>
[CmdletBinding()]
param(
    [string] $Term = $env:MODULE_MATRIX_TERM,
    [string] $Lang = $env:MODULE_MATRIX_LANG
)

$ErrorActionPreference = 'Stop'
$lines = foreach ($entry in @{ TERM = $Term; LANG = $Lang }.GetEnumerator()) {
    if (-not [string]::IsNullOrEmpty($entry.Value)) {
        if ($entry.Value -match '[\r\n]') {
            throw "$($entry.Key) must be a single-line value."
        }
        "$($entry.Key)=$($entry.Value)"
    }
}
if (@($lines).Count -gt 0) {
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
        throw 'GITHUB_ENV is required to configure the test job environment.'
    }
    [IO.File]::AppendAllLines($env:GITHUB_ENV, [string[]] $lines, [Text.UTF8Encoding]::new($false))
}
