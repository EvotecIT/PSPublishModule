$ModuleAssembly = [PowerForge.ModuleLoader.V1.ModuleAssemblyLoadContext]::LoadModuleFromGroup(
    [string[]]{{ModuleAssemblyPaths}},
    {{ModuleAssemblyPath}},
    '{{ContextName}}',
    [string[]]{{SharedAssemblyNames}})
# On a repeat import (Import-Module -Force, or after Remove-Module) PowerShell returns its cached module
# for this assembly without importing the commands here. Drop the cached copy so they import again.
foreach ($StaleModule in @(Get-Module -All | Where-Object { [object]::ReferenceEquals($_.ImplementingAssembly, $ModuleAssembly) })) {
    Remove-Module -ModuleInfo $StaleModule -Force -ErrorAction SilentlyContinue
}
$InnerModule = & $ImportModule -Assembly $ModuleAssembly -Force -PassThru -ErrorAction Stop
{{TypeAcceleratorBlock}}
if ($InnerModule) {
{{ImportedAliasesBlock}}
{{ExportBridgeBlock}}
}
