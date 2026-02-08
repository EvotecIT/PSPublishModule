param([string]$SettingsB64,[Parameter(ValueFromRemainingArguments=$true)][string[]]$Files)
$ErrorActionPreference = 'Stop'
try {
    if (-not (Get-Module -ListAvailable -Name PSScriptAnalyzer)) {
        Write-Output 'PSSA_NOT_FOUND'
        exit 3
    }
    Import-Module PSScriptAnalyzer -ErrorAction Stop
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
    if ($null -ne $settings) {
        $formatted = Invoke-Formatter -ScriptDefinition $text -Settings $settings
    } else {
        $formatted = Invoke-Formatter -ScriptDefinition $text
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
