param([Parameter(Mandatory)][string]$SourcePath,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if((Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'd18b86d9406e900a411641cc69e842bcde2e449e9b8eeb11bc25e5a654ebe3f1'){throw 'Pinned Get-ComputerDiskLogical source hash mismatch'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$source=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $SourcePath).Path)
$helpers=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Functions.psm1'))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$helpers+"`n"+$source,(New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Get-ComputerDiskLogical')}",(New-Object Text.UTF8Encoding $false))
Join-Path $root 'Fixture.psd1'
