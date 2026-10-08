param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$pins=[ordered]@{
 'Public/ActiveDirectory/Convert-ADGuidToSchema.ps1'='c64a3d92889d23c2dba2d433464b4daa1a2bf7e2de3d63b40c10e91f24b1dca0'
 'Public/ActiveDirectory/Convert-ADSchemaToGuid.ps1'='3c864d03e4f64d519cdb41212be88ab9aba6b186658508711f7d231fcd42dbcb'
 'Public/Converts/ConvertTo-ImmutableID.ps1'='0931d254c96baa69b17cdf19d2ef5d5c30c3c6ce3b49c8bcc62a373570cf3d75'
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$payload=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Functions.psm1'))
foreach($pin in $pins.GetEnumerator()) {
 $path=Join-Path $SourceRoot $pin.Key
 if((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $pin.Value){throw "Pinned source mismatch: $($pin.Key)"}
 $payload+="`n"+[IO.File]::ReadAllText((Resolve-Path -LiteralPath $path).Path)
}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$payload,(New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Convert-ADGuidToSchema','Convert-ADSchemaToGuid','ConvertTo-ImmutableID')}",(New-Object Text.UTF8Encoding $false))
Join-Path $root 'Fixture.psd1'
