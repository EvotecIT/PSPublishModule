param(
    [string]$PathsB64,
    [string]$ExcludeB64,
    [string]$OutJson,
    [string]$SkipIfMissing
)
function DecodeLines([string]$b64) {
    if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
    try { 
        $text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
        return $text -split "\n" | Where-Object { $_ -and $_.Trim().Length -gt 0 }
    } catch { return @() }
}
$paths = DecodeLines $PathsB64
$exclude = DecodeLines $ExcludeB64

if (-not (Get-Module -ListAvailable -Name PSScriptAnalyzer)) {
    if ($SkipIfMissing -eq '1') { 'PFVALID::SKIP::PSSA'; exit 0 }
    'PFVALID::ERROR::PSScriptAnalyzer not found.'; exit 2
}
Import-Module PSScriptAnalyzer -ErrorAction Stop
$issues = Invoke-ScriptAnalyzer -Path $paths -ExcludeRule $exclude -ErrorAction Continue
if ($null -eq $issues) { $issues = @() }
$issues | ConvertTo-Json -Depth 6 | Set-Content -Path $OutJson -Encoding UTF8
