param(
  [string]$IncludeNetFxFlag,
  [string]$InstallNet8Flag,
  [string]$ForceFlag
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Write-Encoded([string]$prefix, [string]$value) {
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($value))
  Write-Output ($prefix + $b64)
}

function Get-CredentialProviderPaths {
  $paths = [System.Collections.Generic.List[string]]::new()
  $roots = [System.Collections.Generic.List[string]]::new()

  if ($env:NUGET_PLUGIN_PATHS) {
    foreach ($item in $env:NUGET_PLUGIN_PATHS -split [IO.Path]::PathSeparator) {
      if (-not [string]::IsNullOrWhiteSpace($item)) {
        $roots.Add($item.Trim('"'))
      }
    }
  }

  if ($env:UserProfile) {
    $roots.Add((Join-Path $env:UserProfile '.nuget\plugins'))
  }

  foreach ($root in $roots | Select-Object -Unique) {
    if (-not $root) { continue }

    if (Test-Path -LiteralPath $root -PathType Leaf) {
      $fileName = [IO.Path]::GetFileName($root)
      if ($fileName -in @('CredentialProvider.Microsoft.exe', 'CredentialProvider.Microsoft.dll')) {
        $paths.Add((Resolve-Path -LiteralPath $root).Path)
      }
      continue
    }

    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue) {
      if ($file.Name -in @('CredentialProvider.Microsoft.exe', 'CredentialProvider.Microsoft.dll')) {
        $paths.Add($file.FullName)
      }
    }
  }

  return $paths | Select-Object -Unique
}

function Get-CredentialProviderVersion([string[]]$Paths) {
  foreach ($path in @($Paths)) {
    if ([string]::IsNullOrWhiteSpace($path)) { continue }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }

    try {
      $item = Get-Item -LiteralPath $path -ErrorAction Stop
      $version = $item.VersionInfo.ProductVersion
      if ([string]::IsNullOrWhiteSpace($version)) {
        $version = $item.VersionInfo.FileVersion
      }
      if (-not [string]::IsNullOrWhiteSpace($version)) {
        return $version
      }
    } catch {
    }
  }

  return $null
}

if (-not $IsWindows) {
  Write-Encoded 'PFAZART::ERROR::' 'Automatic Azure Artifacts Credential Provider installation is currently supported on Windows only.'
  exit 2
}

$before = @(Get-CredentialProviderPaths)

try {
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
} catch {
}

try {
  $installScriptContent = Invoke-RestMethod -Uri 'https://aka.ms/install-artifacts-credprovider.ps1' -Headers @{ 'User-Agent' = 'PSPublishModule/PowerForge' }
  $installScript = [scriptblock]::Create($installScriptContent)

  $params = @{}
  if ($InstallNet8Flag -eq '1') { $params.InstallNet8 = $true }
  if ($IncludeNetFxFlag -eq '1') { $params.AddNetfx = $true }
  if ($ForceFlag -eq '1') { $params.Force = $true }

  & $installScript @params | Out-Null

  $after = @(Get-CredentialProviderPaths)
  $changed = (($after | Sort-Object) -join ';') -ne (($before | Sort-Object) -join ';')
  Write-Output ('PFAZART::CHANGED::' + ($(if ($changed) { '1' } else { '0' })))

  foreach ($path in $after) {
    Write-Encoded 'PFAZART::PATH::' $path
  }

  $version = Get-CredentialProviderVersion -Paths $after
  if (-not [string]::IsNullOrWhiteSpace($version)) {
    Write-Encoded 'PFAZART::VERSION::' $version
  }

  if ($after.Count -gt 0) {
    if (-not [string]::IsNullOrWhiteSpace($version)) {
      Write-Encoded 'PFAZART::MESSAGE::' ("Azure Artifacts Credential Provider detected at {0} path(s), version {1}." -f $after.Count, $version)
    } else {
      Write-Encoded 'PFAZART::MESSAGE::' ("Azure Artifacts Credential Provider detected at {0} path(s)." -f $after.Count)
    }
  } else {
    Write-Encoded 'PFAZART::MESSAGE::' 'Azure Artifacts Credential Provider installation completed, but no provider files were detected afterwards.'
  }

  exit 0
} catch {
  Write-Encoded 'PFAZART::ERROR::' $_.Exception.Message
  exit 1
}
