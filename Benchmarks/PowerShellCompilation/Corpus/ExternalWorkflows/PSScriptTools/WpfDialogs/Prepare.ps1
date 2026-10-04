param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$message=Join-Path $SourceRoot 'functions/New-WPFMessageBox.ps1'
$utilities=Join-Path $SourceRoot 'functions/Utilities.ps1'
if((Get-FileHash $message).Hash.ToLowerInvariant() -ne '2f5407426984d6ecefa1e4b4a6ad871002e00cc287ad4575ed2c5c5cba92221d'){throw 'Pinned message source mismatch'}
if((Get-FileHash $utilities).Hash.ToLowerInvariant() -ne '1625373211d326669795cdd8ff2b40381e57147fd07065ca69bbec50cec81592'){throw 'Pinned Utilities source mismatch'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($utilities,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Pinned Utilities parse failed'}
$inputFunction=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-InputBox'},$false))
if($inputFunction.Count -ne 1){throw 'Expected one unchanged input function'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
New-Item -ItemType Directory -Path (Join-Path $root 'functions'),(Join-Path $root 'icons')|Out-Null
[IO.File]::WriteAllText((Join-Path $root 'functions/New-WPFMessageBox.ps1'),[IO.File]::ReadAllText($message),(New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $root 'functions/Invoke-InputBox.ps1'),$inputFunction[0].Extent.Text,(New-Object Text.UTF8Encoding $false))
$payload="function Test-IsPSWindows { return `$true }`n. `"`$PSScriptRoot/functions/New-WPFMessageBox.ps1`"`n. `"`$PSScriptRoot/functions/Invoke-InputBox.ps1`""
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$payload,(New-Object Text.UTF8Encoding $false))
Copy-Item -LiteralPath (Join-Path $SourceRoot 'icons/information.png') -Destination (Join-Path $root 'icons/information.png')
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('New-WPFMessageBox','Invoke-InputBox')}",(New-Object Text.UTF8Encoding $false))
Join-Path $root 'Fixture.psd1'
