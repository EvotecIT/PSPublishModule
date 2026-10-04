param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=Join-Path $SourceRoot 'functions/Test-Expression.ps1'
$xaml=Join-Path $SourceRoot 'functions/form.xaml'
if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne '82dfd6ed313f17dc9122f3f058e21db1d67742fc3ac3cf910e6003bb82636cf3'){
 throw 'Pinned expression source mismatch'
}
if((Get-FileHash -LiteralPath $xaml -Algorithm SHA256).Hash.ToLowerInvariant() -ne '2253fa8f1c00b66dbe8dfcbe42b28b5884ad4ded00afa3b3361ddbe734c6bd4d'){
 throw 'Pinned expression XAML mismatch'
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Pinned expression source parse failed'}
$found=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-ExpressionForm'},$false))
if($found.Count -ne 1){throw 'Expected one unchanged authored form function'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$folder=(New-Item -ItemType Directory -Path (Join-Path $root 'functions')).FullName
[IO.File]::WriteAllText((Join-Path $folder 'Test-ExpressionForm.ps1'),$found[0].Extent.Text,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $xaml -Destination (Join-Path $folder 'form.xaml')
$payload=@'
$script:CapturedExpression = [Collections.Generic.List[object]]::new()
function Test-IsPSWindows { return $true }
function Test-Expression {
    [CmdletBinding()]
    param([scriptblock]$Expression,[int]$Count,[switch]$IncludeExpression,[object[]]$ArgumentList,[double]$Interval,[double]$RandomMinimum,[double]$RandomMaximum)
    if ($Expression.ToString() -ne '40+2') { throw 'Only the owned inert expression is accepted' }
    $script:CapturedExpression.Add([pscustomobject]@{
        Expression = $Expression.ToString(); Count = $Count; IncludeExpression = [bool]$IncludeExpression
        ArgumentCount = $(if ($null -eq $ArgumentList) { 0 } else { $ArgumentList.Count }); Interval = $Interval
    })
    [pscustomobject]@{ Value = 'compiler-result'; Count = $Count; Interval = $Interval }
}
function Get-CapturedExpression { @($script:CapturedExpression) }
. "$PSScriptRoot/functions/Test-ExpressionForm.ps1"
'@
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$payload,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Test-ExpressionForm','Get-CapturedExpression')}",[Text.UTF8Encoding]::new($false))
Join-Path $root 'Fixture.psd1'
