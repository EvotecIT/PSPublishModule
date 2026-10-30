param(
    [Parameter(Mandatory = $true)][string] $Root,
    [Parameter(Mandatory = $true)][string] $Order,
    [Parameter(Mandatory = $true)][int] $ExpectedFeature,
    [switch] $Development,
    [string] $ExistingRuntimeRoot,
    [string] $ExistingPrivateRuntimeRoot,
    [int] $ExistingPrivateFeature = 0
)
$ErrorActionPreference = 'Stop'
if ($Development) { $env:PF_SHARED_FIXTURE_DEVELOPMENT = 'true' }
if ($ExistingRuntimeRoot) {
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $ExistingRuntimeRoot 'Core1\SharedCore.dll'))
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $ExistingRuntimeRoot 'Core0\SharedDisplay.dll'))
}
$expectedSharedContext = $null
if ($ExistingPrivateRuntimeRoot) {
    if ($PSEdition -ne 'Core') { throw 'Private ALC scenario requires Core' }
    $expectedSharedContext = [System.Runtime.Loader.AssemblyLoadContext]::new('Earlier.Shared.Runtime', $false)
    $null = $expectedSharedContext.LoadFromAssemblyPath((Join-Path $ExistingPrivateRuntimeRoot "Core$ExistingPrivateFeature\SharedCore.dll"))
}
$products = $Order -split ','
foreach ($product in $products) {
    Import-Module (Join-Path $Root "$product\$product.psm1") -Force
    $data = & "Get-$product`Data"
    if ($data.Feature -ne $ExpectedFeature -or $data.Private -ne "$product-private") {
        throw "Data contract failed for $product"
    }
    # This is product policy in a synthetic consumer, deliberately outside the loader.
    # Reading attributes does not reference new presentation/core members and is safe on the old ABI.
    $core = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SharedCore' })
    if ($core.Count -ne 1) { throw 'Multiple core instances' }
    $feature = @($core[0].GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -eq 'System.Reflection.AssemblyMetadataAttribute' -and $_.ConstructorArguments[0].Value -eq 'FeatureLevel' })[0].ConstructorArguments[1].Value
    $display = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SharedDisplay' })
    $displayCompatible = $true
    if ($display.Count -gt 0) {
        $displayFeature = @($display[0].GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -eq 'System.Reflection.AssemblyMetadataAttribute' -and $_.ConstructorArguments[0].Value -eq 'FeatureLevel' })[0].ConstructorArguments[1].Value
        $displayCompatible = [int]$displayFeature -ge 1
    }
    if ([int]$feature -ge 1 -and $displayCompatible) {
        $payload = if ($PSEdition -eq 'Core') { 'Core' } else { 'Default' }
        if ($PSEdition -eq 'Core') {
            # Use the product's actual private ALC; shared names must leave that context.
            $hostAssembly = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "$product.Host" })[0]
            $context = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($hostAssembly)
            $presentation = $context.LoadFromAssemblyPath((Join-Path $Root "$product\Lib\$payload\$product.Presentation.dll"))
        } else {
            $presentation = [Reflection.Assembly]::LoadFrom((Join-Path $Root "$product\Lib\$payload\$product.Presentation.dll"))
        }
        $rendered = $presentation.GetType("${product}Presentation.View").GetMethod('Render').Invoke($null, @())
        if ($rendered -notlike 'richdisplay*:1') { throw "Presentation did not bind to the shared runtime: $rendered" }
    }
}
foreach ($name in 'SharedCore', 'SharedDisplay') {
    $loaded = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq $name })
    $expectedCount = if ($name -eq 'SharedDisplay' -and $ExpectedFeature -eq 0) { 0 } else { 1 }
    if ($loaded.Count -ne $expectedCount) { throw "Expected $expectedCount loaded $name, got $($loaded.Count)" }
    if ($loaded.Count -eq 1 -and $PSEdition -eq 'Core') {
        if ($null -eq $expectedSharedContext) { $expectedSharedContext = [System.Runtime.Loader.AssemblyLoadContext]::Default }
        if ([System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($loaded[0]) -ne $expectedSharedContext) {
            throw "$name is not in the canonical shared context"
        }
    }
}
if ($ExpectedFeature -eq 0 -or $ExistingRuntimeRoot) {
    $presentation = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -like '*.Presentation' })
    if ($presentation.Count -ne 0) { throw 'Feature gate loaded new presentation against the old runtime' }
}
if ($PSEdition -eq 'Core') {
    foreach ($product in $products) {
        $hostAssembly = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "$product.Host" })[0]
        $privateAssembly = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "$product.Private" })[0]
        $context = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($hostAssembly)
        if ($context -eq [System.Runtime.Loader.AssemblyLoadContext]::Default -or
            [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($privateAssembly) -ne $context) {
            throw "Private dependency escaped the $product context"
        }
    }
}
foreach ($product in $products) { Remove-Module $product -Force }
"SHARED_IMPORT_OK:$PSEdition`:$Order`:feature$ExpectedFeature"
