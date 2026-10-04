param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=Join-Path $SourceRoot 'functions/New-DynamicParamCode.ps1'
if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne '307660edcaa925e9fba6fe009d40b9f4695cf44d89c5f5e3c1012048e0964c60'){
 throw 'Pinned dynamic-parameter source mismatch'
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Pinned dynamic-parameter source parse failed'}
$names=@('New-PSDynamicParameter','New-PSDynamicParameterForm')
$functions=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in $names},$false))
if($functions.Count -ne 2){throw 'Expected two unchanged authored functions'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$folder=(New-Item -ItemType Directory -Path (Join-Path $root 'functions')).FullName
foreach($function in $functions){
 [IO.File]::WriteAllText((Join-Path $folder "$($function.Name).ps1"),$function.Extent.Text,[Text.UTF8Encoding]::new($false))
}
$payload=@'
$script:CapturedClipboard = [Collections.Generic.List[string]]::new()
function Set-Clipboard {
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)][AllowNull()][object]$Value)
    process { $script:CapturedClipboard.Add([string]$Value) }
}
function Get-CapturedClipboard {
    [pscustomobject]@{ Count = $script:CapturedClipboard.Count; Text = ($script:CapturedClipboard -join "`n") }
}
. "$PSScriptRoot/functions/New-PSDynamicParameter.ps1"
. "$PSScriptRoot/functions/New-PSDynamicParameterForm.ps1"
'@
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$payload,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('New-PSDynamicParameter','New-PSDynamicParameterForm','Get-CapturedClipboard')}",[Text.UTF8Encoding]::new($false))
Join-Path $root 'Fixture.psd1'
