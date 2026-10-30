param([Parameter(Mandatory = $true)][string] $Root)
$ErrorActionPreference = 'Stop'
foreach ($product in 'Alpha', 'Beta') {
    Import-Module (Join-Path $Root "$product/$product.psm1") -Force
    if (@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SharedDisplay' }).Count -ne 0) {
        throw 'The optional display assembly was loaded before presentation requested it.'
    }
}
$payload = if ($PSEdition -eq 'Core') { 'Core' } else { 'Default' }
$path = Join-Path $Root "Beta/Lib/$payload/Beta.Presentation.dll"
if ($PSEdition -eq 'Core') {
    $hostAssembly = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Beta.Host' })[0]
    $context = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($hostAssembly)
    $presentation = $context.LoadFromAssemblyPath($path)
} else {
    $presentation = [Reflection.Assembly]::LoadFrom($path)
}
$rendered = $presentation.GetType('BetaPresentation.View').GetMethod('Render').Invoke($null, @())
if ($rendered -ne 'richdisplay1:1') { throw "Beta used another module's lazy adapter: $rendered" }
"SELECTED_LAZY_DISPLAY_OK:$PSEdition"
