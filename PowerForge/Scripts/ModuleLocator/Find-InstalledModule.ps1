param(
  [string]$Name,
  [string]$RequiredVersion,
  [string]$MinimumVersion,
  [string]$MaximumVersion,
  [string]$InstallScope
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

    if (-not [string]::IsNullOrWhiteSpace($InstallScope)) {
      $scopeRoots = @()
      if ($InstallScope -eq 'CurrentUser') {
        $documents = [Environment]::GetFolderPath('MyDocuments')
        if ($documents) {
          $scopeRoots += (Join-Path $documents 'PowerShell/Modules')
          $scopeRoots += (Join-Path $documents 'WindowsPowerShell/Modules')
        }
        if ($HOME) {
          $scopeRoots += (Join-Path $HOME '.local/share/powershell/Modules')
        }
      } elseif ($InstallScope -eq 'AllUsers') {
        $programFiles = [Environment]::GetFolderPath('ProgramFiles')
        if ($programFiles) {
          $scopeRoots += (Join-Path $programFiles 'PowerShell/Modules')
          $scopeRoots += (Join-Path $programFiles 'WindowsPowerShell/Modules')
        }
        $scopeRoots += '/usr/local/share/powershell/Modules'
        $scopeRoots += '/opt/microsoft/powershell/7/Modules'
      }

      $normalizedRoots = @($scopeRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        try { [System.IO.Path]::GetFullPath($_).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) } catch { $null }
      } | Where-Object { $_ })
      if ($normalizedRoots.Count -gt 0) {
        $filtered = $filtered | Where-Object {
          if (-not $_.ModuleBase) { return $false }
          $moduleBase = [System.IO.Path]::GetFullPath($_.ModuleBase).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
          foreach ($root in $normalizedRoots) {
            if ($moduleBase.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
          }
          return $false
        }
      } else {
        $filtered = @()
      }
    }

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
