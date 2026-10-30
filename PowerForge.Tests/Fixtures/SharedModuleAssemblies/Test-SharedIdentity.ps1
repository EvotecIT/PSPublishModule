param([Parameter(Mandatory = $true)][string] $Root)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $Root 'Alpha/Alpha.psm1') -Force
$payload = if ($PSEdition -eq 'Core') { 'Core' } else { 'Default' }
$directory = Join-Path $Root "Alpha/Lib/$payload"
$requested = [Reflection.AssemblyName]::new('SharedDisplay, Version=1.0.0.0, Culture=neutral, PublicKeyToken=0123456789abcdef')
try {
    $null = [PowerForge.Generated.SharedAssemblyPolicyV3]::Resolve($requested, [string[]]@($directory))
    throw 'The mismatched public key token was accepted.'
} catch [IO.FileLoadException] { }
if (@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SharedDisplay' }).Count -ne 0) {
    throw 'The wrong shared assembly was loaded before identity rejection.'
}
if ($PSEdition -ne 'Core') {
    $module = Get-Module Alpha
    $resolver = & $module { $PowerForgeDesktopAssemblyResolverState }
    if ($null -eq $resolver) { throw 'Desktop resolver was not registered.' }
    $hostAssembly = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Alpha.Host' })[0]
    $flags = [Reflection.BindingFlags]'Instance, NonPublic'
    $method = $resolver.GetType().GetMethod('Resolve', $flags)
    $arguments = [ResolveEventArgs]::new($requested.FullName, $hostAssembly)
    if ($null -ne $method.Invoke($resolver, @($null, $arguments))) { throw 'Desktop resolver accepted the mismatched identity.' }
    if (@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SharedDisplay' }).Count -ne 0) {
        throw 'Desktop resolver loaded the wrong shared assembly.'
    }
}
"SHARED_IDENTITY_OK:$PSEdition"
