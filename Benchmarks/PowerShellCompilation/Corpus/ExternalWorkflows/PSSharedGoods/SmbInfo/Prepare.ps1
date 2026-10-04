param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$paths=@('Private/Get-ComputerSMBInfo.ps1','Public/Computers/Get-ComputerSMBShareList.ps1')
$hashes=@('3ccd899673eae4228034ce10b2756e6a65a1a7d6a8596910ef6a84bff912760d','29ef551916644583302e8131e5dd0d5d977cd9104670a9cfa546a1e22bdf13c5')
$payload=@()
for($i=0;$i -lt $paths.Count;$i++){
 $path=Join-Path $SourceRoot $paths[$i]
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hashes[$i]){throw 'Pinned SMB source hash mismatch'}
 $payload+=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $path).Path)
}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$encoding=New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),($payload -join "`n"),$encoding)
$definition=[regex]::Match($payload[1],"(?s)@'\r?\n(using System;.*?)\r?\n'@").Groups[1].Value
if(-not $definition){throw 'Pinned native definition missing'}
[IO.File]::WriteAllText((Join-Path $root 'NativeDefinition.cs'),$definition,$encoding)
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Get-ComputerSMBInfo','Get-ComputerSMBShareList')}",$encoding)
Join-Path $root 'Fixture.psd1'
