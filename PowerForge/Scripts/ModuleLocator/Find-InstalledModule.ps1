param(
  [string]$Name,
  [string]$RequiredVersion,
  [string]$MinimumVersion,
  [string]$MaximumVersion
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Enc([string]$s) {
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(([string]$s)))
}

try {
  $mods = Get-Module -ListAvailable -Name $Name -ErrorAction SilentlyContinue
  if ($mods) {
    $filtered = $mods

    if (-not [string]::IsNullOrWhiteSpace($RequiredVersion)) {
      $req = [version]$RequiredVersion
      $filtered = $filtered | Where-Object { $_.Version -eq $req }
    } else {
      if (-not [string]::IsNullOrWhiteSpace($MinimumVersion)) {
        $min = [version]$MinimumVersion
        $filtered = $filtered | Where-Object { $_.Version -ge $min }
      }
      if (-not [string]::IsNullOrWhiteSpace($MaximumVersion)) {
        $max = [version]$MaximumVersion
        $filtered = $filtered | Where-Object { $_.Version -le $max }
      }
    }

    $latest = ($filtered | Sort-Object Version -Descending | Select-Object -First 1)
    if ($latest -and $latest.ModuleBase) {
      $ver = if ($latest.Version) { [string]$latest.Version } else { '' }
      Write-Output ('PFMODLOC::FOUND::' + (Enc $ver) + '::' + (Enc $latest.ModuleBase))
    }
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  Write-Output ('PFMODLOC::ERROR::' + (Enc $msg))
  exit 1
}

