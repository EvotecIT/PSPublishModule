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

function Write-LoaderDiagnostics([System.Exception]$Exception) {
  if ($null -eq $Exception) { return }

  if ($Exception -is [System.Reflection.ReflectionTypeLoadException]) {
    $messages = @(
      foreach ($loaderException in @($Exception.LoaderExceptions)) {
        if ($null -eq $loaderException) { continue }
        $message = [string]$loaderException.Message
        if ([string]::IsNullOrWhiteSpace($message)) { continue }
        $message.Trim()
      }
    ) | Select-Object -Unique

    foreach ($message in $messages) {
      Write-Output ('PFIMPORT::LOADERERROR::' + $message)
    }
  }

  if ($Exception.InnerException) {
    Write-LoaderDiagnostics -Exception $Exception.InnerException
  }
}

function Reset-PSModulePathForEdition {
  $runningOnWindows = $false
  try {
    $runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
  } catch {
    $runningOnWindows = ($env:OS -eq 'Windows_NT')
  }

  if (-not $runningOnWindows) { return }

  $documents = [Environment]::GetFolderPath('MyDocuments')
  if ([string]::IsNullOrWhiteSpace($documents)) {
    $documents = [Environment]::GetFolderPath('UserProfile')
  }

  $userModules = @(
    Join-Path $documents 'PowerShell\Modules'
    Join-Path $documents 'WindowsPowerShell\Modules'
  )

  $programFiles = $env:ProgramFiles
  $sharedModules = @(
    Join-Path $programFiles 'PowerShell\Modules'
    Join-Path $programFiles 'WindowsPowerShell\Modules'
  )

  $psHomeModules = Join-Path $PSHOME 'Modules'
  $paths = @(
    $userModules
    $sharedModules
    $psHomeModules
  ) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique

  if ($paths.Count -gt 0) {
    $env:PSModulePath = ($paths -join [IO.Path]::PathSeparator)
  }
}

function Initialize-DesktopImportCapacity {
  if ($PSVersionTable.PSEdition -ne 'Desktop') { return }

  $targetFunctionCount = 18000
  $targetVariableCount = 18000

  try {
    if (($MaximumFunctionCount -as [int]) -lt $targetFunctionCount) {
      $script:MaximumFunctionCount = $targetFunctionCount
    }
  } catch {
    $script:MaximumFunctionCount = $targetFunctionCount
  }

  try {
    if (($MaximumVariableCount -as [int]) -lt $targetVariableCount) {
      $script:MaximumVariableCount = $targetVariableCount
    }
  } catch {
    $script:MaximumVariableCount = $targetVariableCount
  }

  Write-Verbose "Raised Windows PowerShell import limits to Function=$MaximumFunctionCount Variable=$MaximumVariableCount."
}

try {
  Reset-PSModulePathForEdition
  Initialize-DesktopImportCapacity

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
  try { Write-Output ('PFIMPORT::ERRORTYPE::' + [string]$_.Exception.GetType().FullName) } catch { }
  try { Write-Output ('PFIMPORT::ERROR::' + [string]$_.Exception.Message) } catch { }
  try { Write-LoaderDiagnostics -Exception $_.Exception } catch { }
  throw
}
