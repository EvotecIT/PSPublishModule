param([Parameter(Mandatory)][string]$SourcePath,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if((Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne 'cf4b9cb1c07935b1a2df1790de674356236027eade8eac8e5e480b7aee0a3276'){throw 'Pinned password source hash mismatch'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$source=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $SourcePath).Path)
$definition=[regex]::Match($source,"(?s)@'\r?\n(\[DllImport.*?);\r?\n'@").Groups[1].Value+';'
if($definition -notlike '[[]DllImport*'){throw 'Authored native declaration missing'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$encoding=New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$source,$encoding)
[IO.File]::WriteAllText((Join-Path $root 'NativeDeclaration.txt'),$definition,$encoding)
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Set-PasswordRemotely')}",$encoding)
Join-Path $root 'Fixture.psd1'
