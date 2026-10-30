# Shared names are policy, not binary exports. Only the explicit preload subset runs here.
$PowerForgeSharedPolicyLock = [string]::Intern('PowerForge.Generated.SharedAssemblyPolicyV3')
[Threading.Monitor]::Enter($PowerForgeSharedPolicyLock)
try {
    if (-not ('PowerForge.Generated.SharedAssemblyPolicyV3' -as [type])) {
        Add-Type -TypeDefinition @'
{{SharedAssemblyPolicySource}}
'@ -ErrorAction Stop
    }
} finally {
    [Threading.Monitor]::Exit($PowerForgeSharedPolicyLock)
}
$PowerForgeSharedAssemblyDirectories = {{SharedAssemblyDirectories}}
[PowerForge.Generated.SharedAssemblyPolicyV3]::Register([string[]]{{SharedAssemblies}}, [string[]]$PowerForgeSharedAssemblyDirectories)
foreach ($PowerForgeSharedAssemblyName in {{PreloadSharedAssemblies}}) {
    $null = [PowerForge.Generated.SharedAssemblyPolicyV3]::Resolve($PowerForgeSharedAssemblyName, [string[]]$PowerForgeSharedAssemblyDirectories)
}
