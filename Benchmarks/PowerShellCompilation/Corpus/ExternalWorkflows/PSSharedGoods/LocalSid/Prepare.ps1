param([Parameter(Mandatory)][string]$SourcePath,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if((Get-FileHash -LiteralPath $SourcePath).Hash.ToLowerInvariant() -ne '5e614cd7d9bf3545f1c1851805e7d94010a8e37497b1b3c0a00cc6e55853b2a7'){throw 'Pinned Get-LocalComputerSid source mismatch'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),[IO.File]::ReadAllText((Resolve-Path $SourcePath).Path),(New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Get-LocalComputerSid')}",(New-Object Text.UTF8Encoding $false))
Join-Path $root 'Fixture.psd1'
