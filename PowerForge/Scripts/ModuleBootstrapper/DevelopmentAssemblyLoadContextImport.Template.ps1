                $PowerForgeDevelopmentModuleAssembly = [PowerForge.ModuleLoader.V1.ModuleAssemblyLoadContext]::LoadModuleFromGroup(
                    [string[]]@($PowerForgeDevelopmentBinaryPath),
                    $PowerForgeDevelopmentBinaryPath,
                    '{{DevelopmentContextName}}',
                    [string[]]{{SharedAssemblyNames}})
                $PowerForgeDevelopmentInnerModule = & $ImportModule -Assembly $PowerForgeDevelopmentModuleAssembly -Force -PassThru -ErrorAction Stop
{{TypeAcceleratorSetupBlock}}
                if ($PowerForgeDevelopmentInnerModule) {
{{ExportBridgeBlock}}
                }
