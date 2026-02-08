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

$names = DecodeLines $NamesB64
foreach ($n in $names) {
  try {
    $m = Get-Module -ListAvailable -Name $n | Sort-Object Version -Descending | Select-Object -First 1
    $ver = if ($m) { [string]$m.Version } else { '' }
    $guid = if ($m) { [string]$m.Guid } else { '' }
    $fields = @($n, $ver, $guid) | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFMODINFO::ITEM::' + ($fields -join '::'))
  } catch {
    $fields = @($n, '', '') | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFMODINFO::ITEM::' + ($fields -join '::'))
  }
}
exit 0
