param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$files=@{
 'ConvertTo-WPFGrid.ps1'='ee46ea4187323dac24099d9956268dc608fdb3b7ee1f7e9553d9fff517e96b89'
 'Utilities.ps1'='1625373211d326669795cdd8ff2b40381e57147fd07065ca69bbec50cec81592'
 'Test-IsWindows.ps1'='8514f3cb7e47f21aee2d4c9f3773b7d45c18d9b863f7dd0b7bb01c17be409f1d'
}
foreach($name in $files.Keys){
 $path=Join-Path $SourceRoot "functions/$name"
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $files[$name]){throw "Pinned $name mismatch"}
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $SourceRoot 'functions/Utilities.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Pinned Utilities parse failed'}
$helper=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'New-RunspaceCleanupJob'},$false))
if($helper.Count -ne 1){throw 'Expected one unchanged cleanup helper'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$functions=(New-Item -ItemType Directory -Path (Join-Path $root 'functions')).FullName
Copy-Item -LiteralPath (Join-Path $SourceRoot 'functions/ConvertTo-WPFGrid.ps1') -Destination $functions
Copy-Item -LiteralPath (Join-Path $SourceRoot 'functions/Test-IsWindows.ps1') -Destination $functions
[IO.File]::WriteAllText((Join-Path $functions 'New-RunspaceCleanupJob.ps1'),$helper[0].Extent.Text,(New-Object Text.UTF8Encoding $false))
$payload=@'
. "$PSScriptRoot/functions/Test-IsWindows.ps1"
. "$PSScriptRoot/functions/New-RunspaceCleanupJob.ps1"
. "$PSScriptRoot/functions/ConvertTo-WPFGrid.ps1"
'@
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$payload,(New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('ConvertTo-WPFGrid')}",(New-Object Text.UTF8Encoding $false))
Join-Path $root 'Fixture.psd1'
