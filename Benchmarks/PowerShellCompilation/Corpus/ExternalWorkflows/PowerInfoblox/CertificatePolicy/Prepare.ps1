param([Parameter(Mandatory)][string]$SourcePath,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if((Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne '941e9b14af6083304002e0cb0e4ab435cbdc203537673cc2342be8fb45c3b16b'){throw 'Pinned Hide-SelfSignedCerts source hash mismatch'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$source=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $SourcePath).Path)
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$source,(New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Hide-SelfSignedCerts')}",(New-Object Text.UTF8Encoding $false))
Join-Path $root 'Fixture.psd1'
