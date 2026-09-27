param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$pins=[ordered]@{'Public/Objects/Copy-DictionaryManual.ps1'='755c5f8be375ce49029ebb04571e53082bd7f1e36e27620d76f53a1d15fe3625';'Public/ActiveDirectory/Get-WinADForestDetails.ps1'='112b8ca5e27dc1e492d5f950bdd088065e2534d0b649de8a98d60d928dcb8400'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$payload=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Functions.psm1'))
foreach($pin in $pins.GetEnumerator()){$path=Join-Path $SourceRoot $pin.Key;if((Get-FileHash $path).Hash.ToLowerInvariant() -ne $pin.Value){throw "Pinned source mismatch $($pin.Key)"};$payload+="`n"+[IO.File]::ReadAllText($path)}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$payload,(New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Get-WinADForestDetails')}",(New-Object Text.UTF8Encoding $false))
Join-Path $root 'Fixture.psd1'
