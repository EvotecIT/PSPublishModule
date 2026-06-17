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

function ConvertTo-PssaIssueRecord {
    param(
        [Parameter(ValueFromPipeline)]
        $Issue
    )

    process {
        if ($null -eq $Issue) { return }

        $extent = $Issue.Extent
        $path = if (-not [string]::IsNullOrWhiteSpace($Issue.ScriptPath)) {
            $Issue.ScriptPath
        } elseif (-not [string]::IsNullOrWhiteSpace($Issue.ScriptName)) {
            $Issue.ScriptName
        } elseif ($null -ne $extent -and -not [string]::IsNullOrWhiteSpace($extent.File)) {
            $extent.File
        } else {
            ''
        }

        $correction = ''
        $suggestedCorrections = @($Issue.SuggestedCorrections)
        if ($suggestedCorrections.Count -gt 0 -and $null -ne $suggestedCorrections[0]) {
            if (-not [string]::IsNullOrWhiteSpace($suggestedCorrections[0].Description)) {
                $correction = $suggestedCorrections[0].Description
            } elseif (-not [string]::IsNullOrWhiteSpace($suggestedCorrections[0].Text)) {
                $correction = $suggestedCorrections[0].Text
            }
        }

        [pscustomobject]@{
            RuleName = [string]$Issue.RuleName
            Severity = [string]$Issue.Severity
            Message = [string]$Issue.Message
            ScriptPath = [string]$path
            Line = if ($null -ne $extent) { [int]$extent.StartLineNumber } else { 0 }
            Column = if ($null -ne $extent) { [int]$extent.StartColumnNumber } else { 0 }
            EndLine = if ($null -ne $extent) { [int]$extent.EndLineNumber } else { 0 }
            EndColumn = if ($null -ne $extent) { [int]$extent.EndColumnNumber } else { 0 }
            SuggestedCorrection = [string]$correction
        }
    }
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
                # Sentinel consumed by ModuleValidationService.Checks.cs.
                'PFVALID::SKIP::PSSA-CONFLICT'
                exit 0
            } else {
                # Strict mode still fails when the analyzer command never became available.
                throw
            }
        }
    }

    $issues = Invoke-ScriptAnalyzer -Path $paths -ExcludeRule $exclude -ErrorAction Continue
    if ($null -eq $issues) { $issues = @() }
    $records = @($issues | ConvertTo-PssaIssueRecord)
    $json = if ($records.Count -eq 0) { '[]' } else { $records | ConvertTo-Json -Depth 5 }
    Set-Content -Path $OutJson -Value $json -Encoding UTF8 -ErrorAction Stop
} catch {
    $message = if ($_.Exception) { $_.Exception.Message } else { "$_" }
    "PFVALID::ERROR::$message"
    exit 2
}
