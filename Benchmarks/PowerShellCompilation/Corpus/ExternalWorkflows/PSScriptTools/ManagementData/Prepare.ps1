param([Parameter(Mandatory)][string]$SourcePath,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$expected='6a13743f5de27e510a9cce5f90ea6a1a98859280132e8307ef5c9c0e0faac1f2'
if((Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'Pinned Find-CimClass source hash mismatch'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$helpers=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Functions.psm1'))
$source=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $SourcePath).Path)
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$helpers+"`n"+$source,(New-Object Text.UTF8Encoding $false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Fixture.psd1') -Destination (Join-Path $root 'Fixture.psd1')
Join-Path $root 'Fixture.psd1'
