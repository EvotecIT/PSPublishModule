# Shared names are policy, not binary exports. Only the explicit preload subset runs here.
$PowerForgeSharedAssemblyDirectories = {{SharedAssemblyDirectories}}
[PowerForge.ModuleLoader.V1.SharedAssemblyPolicy]::Register([string[]]{{SharedAssemblies}}, [string[]]$PowerForgeSharedAssemblyDirectories)
foreach ($PowerForgeSharedAssemblyName in {{PreloadSharedAssemblies}}) {
    $null = [PowerForge.ModuleLoader.V1.SharedAssemblyPolicy]::Resolve([string]$PowerForgeSharedAssemblyName, [string[]]$PowerForgeSharedAssemblyDirectories)
}
