function Write-PFNuGetInfo([string]$Message) {
  Write-Output ('PFPWSGET::INFO::' + [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Message)))
}

function Initialize-PFNuGetProvider {
  # PowerShellGet asks before downloading the NuGet provider, and -NonInteractive turns that question into a
  # confusing failure. Install it up front without prompting, as Install-Module would after answering yes.
  $minimum = [version]'2.8.5.201'
  # Without -Name this lists loaded providers only: fast, and it never starts PackageManagement's own bootstrap prompt.
  $available = @(Get-PackageProvider -ErrorAction SilentlyContinue -WarningAction SilentlyContinue |
    Where-Object { $_.Name -eq 'NuGet' -and $_.Version -ge $minimum })
  if ($available.Count -eq 0) {
    # Installed but not loaded yet. Listing is slow with many modules, so it only runs when needed.
    $available = @(Get-PackageProvider -Name NuGet -ListAvailable -ErrorAction SilentlyContinue -WarningAction SilentlyContinue |
      Where-Object { $_.Version -ge $minimum })
  }
  if ($available.Count -eq 0) {
    Write-PFNuGetInfo 'Installing the NuGet package provider that PowerShellGet needs (one-time download).'
    Install-PackageProvider -Name NuGet -MinimumVersion $minimum -Scope CurrentUser -Force -ErrorAction Stop | Out-Null
    # An installed provider is imported by PowerShellGet on first use; importing it here as well is slow on pwsh.
    Import-PackageProvider -Name NuGet -MinimumVersion $minimum -Force -ErrorAction SilentlyContinue | Out-Null
  }
}

function Get-PFNuGetClientPaths($Module) {
  $paths = @(& $Module { @([string]$script:NuGetExePath, [string]$script:DotnetCommandPath) })
  foreach ($root in @($env:LOCALAPPDATA, $env:ProgramData)) {
    if (-not [string]::IsNullOrWhiteSpace($root)) {
      $paths += (Join-Path $root 'Microsoft\Windows\PowerShell\PowerShellGet\NuGet.exe')
    }
  }
  # Only PowerShellGet versions that know the dotnet CLI can publish without NuGet.exe.
  $names = @('nuget.exe')
  if (& $Module { -not [string]::IsNullOrWhiteSpace([string]$script:DotnetCommandName) }) { $names += 'dotnet' }
  foreach ($name in $names) {
    $command = Get-Command -Name $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { $paths += $command.Path }
  }
  return @($paths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) })
}

function Initialize-PFNuGetExe {
  [CmdletBinding()]
  param()
  # Publish-Module also needs NuGet.exe (or the dotnet CLI on newer PowerShellGet) and asks before downloading it.
  $module = Get-Module PowerShellGet | Select-Object -First 1
  if ($null -eq $module) { return }
  $bootstrap = & $module { Get-Command -Name Install-NuGetClientBinaries -ErrorAction SilentlyContinue }
  if ($null -eq $bootstrap -or -not $bootstrap.Parameters.ContainsKey('Force')) { return }

  # With NuGet.exe or dotnet present PowerShellGet does not prompt, so leave the client setup to Publish-Module.
  if (@(Get-PFNuGetClientPaths $module).Count -gt 0) { return }
  Write-PFNuGetInfo 'Downloading NuGet.exe, which PowerShellGet needs to publish modules (one-time download).'
  & $module { param($caller) Install-NuGetClientBinaries -CallerPSCmdlet $caller -BootstrapNuGetExe -Force } $PSCmdlet
  if (@(Get-PFNuGetClientPaths $module).Count -eq 0) {
    $target = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { 'a folder on PATH' } else { Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\PowerShell\PowerShellGet' }
    throw ('PowerShellGet needs NuGet.exe to publish modules, but it is not installed and could not be downloaded. ' +
      'Download https://dist.nuget.org/win-x86-commandline/latest/nuget.exe to ' + $target +
      ', or publish with PSResourceGet instead.')
  }
}
