param(
  [string]$ModulesB64,
  [string]$ImportRequired,
  [string]$ImportSelf,
  [string]$ModulePath,
  [string]$VerboseFlag
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$importVerbose = ($VerboseFlag -eq '1')
$VerbosePreference = if ($importVerbose) { 'Continue' } else { 'SilentlyContinue' }

function DecodeModules([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
  if ([string]::IsNullOrWhiteSpace($json)) { return @() }
  try { return $json | ConvertFrom-Json } catch { return @() }
}

try {
  if ($ImportRequired -eq '1') {
    $modules = DecodeModules $ModulesB64
    foreach ($m in $modules) {
      if (-not $m -or [string]::IsNullOrWhiteSpace($m.Name)) { continue }
      if ($m.RequiredVersion) {
        Import-Module -Name $m.Name -RequiredVersion $m.RequiredVersion -Force -ErrorAction Stop -Verbose:$importVerbose
      } elseif ($m.MinimumVersion) {
        Import-Module -Name $m.Name -MinimumVersion $m.MinimumVersion -Force -ErrorAction Stop -Verbose:$importVerbose
      } else {
        Import-Module -Name $m.Name -Force -ErrorAction Stop -Verbose:$importVerbose
      }
    }
  }

  if ($ImportSelf -eq '1') {
    if (-not [string]::IsNullOrWhiteSpace($ModulePath)) {
      Import-Module -Name $ModulePath -Force -ErrorAction Stop -Verbose:$importVerbose
    } else {
      throw 'ModulePath is required for ImportSelf.'
    }
  }

  exit 0
} catch {
  Write-Output 'PFIMPORT::FAILED'
  try { Write-Output ('PFIMPORT::PSVERSION::' + [string]$PSVersionTable.PSVersion) } catch { }
  try { Write-Output ('PFIMPORT::PSEDITION::' + [string]$PSVersionTable.PSEdition) } catch { }
  try { Write-Output 'PFIMPORT::PSMODULEPATH::BEGIN'; Write-Output ([string]$env:PSModulePath); Write-Output 'PFIMPORT::PSMODULEPATH::END' } catch { }
  try { Write-Output ('PFIMPORT::ERROR::' + [string]$_.Exception.Message) } catch { }
  throw
}
