param(
  [string]$NamesB64
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function DecodeLines([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
  return $text -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function Enc([string]$s) {
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(([string]$s)))
}

try {
  $names = DecodeLines $NamesB64
  foreach ($n in $names) {
    $mods = Get-Module -ListAvailable -Name $n -ErrorAction SilentlyContinue
    $ver = ''
    if ($mods) {
      $latest = ($mods | Sort-Object Version -Descending | Select-Object -First 1)
      if ($latest -and $latest.Version) { $ver = [string]$latest.Version }
    }
    Write-Output ('PFMOD::ITEM::' + (Enc $n) + '::' + (Enc $ver))
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 1
}

