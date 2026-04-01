param(
    [string]$PathsB64,
    [string]$ExcludeB64,
    [string]$OutJson,
    [string]$SkipIfMissing
)

$ErrorActionPreference = 'Stop'

function DecodeLines([string]$b64) {
    if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
    try {
        $text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
        return $text -split "\n" | Where-Object { $_ -and $_.Trim().Length -gt 0 }
    } catch {
        'PFVALID::ERROR::Failed to decode ScriptAnalyzer arguments.'
        exit 2
    }
}

function Test-PssaAssemblyConflict([string]$message) {
    if ([string]::IsNullOrWhiteSpace($message)) { return $false }

    return $message.IndexOf('PSScriptAnalyzer', [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $message.IndexOf('already loaded', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

try {
    $paths = DecodeLines $PathsB64
    $exclude = DecodeLines $ExcludeB64

    if (-not (Get-Module -ListAvailable -Name PSScriptAnalyzer)) {
        if ($SkipIfMissing -eq '1') { 'PFVALID::SKIP::PSSA'; exit 0 }
        'PFVALID::ERROR::PSScriptAnalyzer not found.'
        exit 2
    }

    if (-not (Get-Command -Name Invoke-ScriptAnalyzer -ErrorAction SilentlyContinue)) {
        try {
            Import-Module PSScriptAnalyzer -ErrorAction Stop
        } catch {
            $message = if ($_.Exception) { $_.Exception.Message } else { "$_" }
            $commandAvailable = $null -ne (Get-Command -Name Invoke-ScriptAnalyzer -ErrorAction SilentlyContinue)

            if ($commandAvailable -and (Test-PssaAssemblyConflict $message)) {
                # The analyzer command is already available from an existing load context.
            } elseif (($SkipIfMissing -eq '1') -and (Test-PssaAssemblyConflict $message)) {
                'PFVALID::SKIP::PSSA-CONFLICT'
                exit 0
            } else {
                throw
            }
        }
    }

    $issues = Invoke-ScriptAnalyzer -Path $paths -ExcludeRule $exclude -ErrorAction Continue
    if ($null -eq $issues) { $issues = @() }
    $json = if (@($issues).Count -eq 0) { '[]' } else { @($issues) | ConvertTo-Json -Depth 6 }
    Set-Content -Path $OutJson -Value $json -Encoding UTF8 -ErrorAction Stop
} catch {
    $message = if ($_.Exception) { $_.Exception.Message } else { "$_" }
    "PFVALID::ERROR::$message"
    exit 2
}
