                $PowerForgeDevelopmentModuleAssembly = [PowerForge.ModuleLoader.V1.ModuleAssemblyLoadContext]::LoadModuleFromGroup(
                    [string[]]@($PowerForgeDevelopmentBinaryPath),
                    $PowerForgeDevelopmentBinaryPath,
                    '{{DevelopmentContextName}}',
                    [string[]]{{SharedAssemblyNames}})
                foreach ($PowerForgeStaleInnerModule in @(Get-Module -All | Where-Object { [object]::ReferenceEquals($_.ImplementingAssembly, $PowerForgeDevelopmentModuleAssembly) })) {
                    Remove-Module -ModuleInfo $PowerForgeStaleInnerModule -Force -ErrorAction SilentlyContinue
                }
                $PowerForgeDevelopmentInnerModule = & $ImportModule -Assembly $PowerForgeDevelopmentModuleAssembly -Force -PassThru -ErrorAction Stop
{{TypeAcceleratorSetupBlock}}
                if ($PowerForgeDevelopmentInnerModule) {
{{ExportBridgeBlock}}
                }
