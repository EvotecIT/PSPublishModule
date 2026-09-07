param([string]$SettingsB64,[Parameter(ValueFromRemainingArguments=$true)][string[]]$Files)
$ErrorActionPreference = 'Stop'
try {
    $ModuleRoots = @($env:PSModulePath -split [System.IO.Path]::PathSeparator |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $PssaCandidates = foreach ($ModuleRoot in $ModuleRoots) {
        $PssaRoot = [System.IO.Path]::Combine($ModuleRoot, 'PSScriptAnalyzer')
        $DirectManifest = [System.IO.Path]::Combine($PssaRoot, 'PSScriptAnalyzer.psd1')
        if ([System.IO.File]::Exists($DirectManifest)) {
            [pscustomobject]@{ Path = $DirectManifest; Version = [version]'0.0' }
        }
        if ([System.IO.Directory]::Exists($PssaRoot)) {
            foreach ($VersionDirectory in [System.IO.Directory]::EnumerateDirectories($PssaRoot)) {
                $Version = $null
                if (-not [version]::TryParse([System.IO.Path]::GetFileName($VersionDirectory), [ref]$Version)) {
                    continue
                }
                $VersionedManifest = [System.IO.Path]::Combine($VersionDirectory, 'PSScriptAnalyzer.psd1')
                if ([System.IO.File]::Exists($VersionedManifest)) {
                    [pscustomobject]@{ Path = $VersionedManifest; Version = $Version }
                }
            }
        }
    }
    $PssaModulePath = $PssaCandidates |
        Sort-Object -Property Version -Descending |
        Select-Object -ExpandProperty Path -First 1
    if (-not $PssaModulePath) {
        Write-Output 'PSSA_NOT_FOUND'
        exit 3
    }
    # Keep unrelated discoverable modules from affecting PSSA's AST/type analysis.
    # Discovery above is filesystem-only; PowerShell module discovery begins after isolation.
    $env:PSModulePath = [System.IO.Path]::Combine($PSHOME, 'Modules')
    Import-Module -Name $PssaModulePath -ErrorAction Stop
} catch {
    Write-Output 'PSSA_NOT_FOUND'
    exit 3
}

$settings = $null
if ($SettingsB64) {
  try {
    $json = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($SettingsB64))
    $settings = ConvertFrom-Json -InputObject $json
  } catch {
    $settings = $null
  }
}

function ConvertTo-Hashtable {
  param([object]$InputObject)
  if ($null -eq $InputObject) { return $null }
  if ($InputObject -is [System.Collections.IDictionary]) { return $InputObject }
  if ($InputObject -is [pscustomobject]) {
    $h = @{}
    foreach ($p in $InputObject.PSObject.Properties) {
      $h[$p.Name] = ConvertTo-Hashtable $p.Value
    }
    return $h
  }
  if ($InputObject -is [System.Collections.IEnumerable] -and -not ($InputObject -is [string])) {
    $arr = @()
    foreach ($i in $InputObject) { $arr += ConvertTo-Hashtable $i }
    return ,$arr
  }
  return $InputObject
}
if ($null -ne $settings) { $settings = ConvertTo-Hashtable $settings }

foreach ($f in $Files) {
  try {
    $text = Get-Content -LiteralPath $f -Raw -ErrorAction Stop
    $formatterErrors = @()
    if ($null -ne $settings) {
        $formatted = Invoke-Formatter -ScriptDefinition $text -Settings $settings -ErrorAction SilentlyContinue -ErrorVariable +formatterErrors
    } else {
        $formatted = Invoke-Formatter -ScriptDefinition $text -ErrorAction SilentlyContinue -ErrorVariable +formatterErrors
    }
    if ($formatterErrors.Count -gt 0) {
        $unexpectedErrors = @($formatterErrors | Where-Object {
            $message = $_.Exception.Message
            $message -notlike '*PowerShellCustomFunctionAttribute*' -or
            $message -notlike '*FunctionMemberAst*' -or
            $message -notlike '*FunctionDefinitionAst*'
        })
        if ($unexpectedErrors.Count -gt 0 -or $null -eq $formatted) {
            $messages = @($formatterErrors | ForEach-Object { $_.Exception.Message }) -join '; '
            throw $messages
        }
        # PowerShell 7.6 can surface this non-terminating engine error while
        # PSScriptAnalyzer's PSUseCorrectCasing rule inspects class method calls.
        # Invoke-Formatter still returns its complete formatted script, so retain
        # that output while making the compatibility fallback visible to callers.
        Write-Output ("WARNING::" + $f + "::PSScriptAnalyzer skipped class-member command casing because the current PowerShell runtime could not inspect FunctionMemberAst metadata.")
    }
    if ($null -ne $formatted -and $formatted -ne $text) {
        [System.IO.File]::WriteAllText($f, $formatted, [System.Text.UTF8Encoding]::new($true))
        Write-Output ("FORMATTED::" + $f)
    }
    else {
        Write-Output ("UNCHANGED::" + $f)
    }
  } catch {
    Write-Output ("ERROR::" + $f + "::" + $_.Exception.Message)
  }
}
exit 0
