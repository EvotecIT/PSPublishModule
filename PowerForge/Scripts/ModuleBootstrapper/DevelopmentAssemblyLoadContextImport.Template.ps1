                if (-not ('{{LoaderTypeName}}' -as [type])) {
                    Add-Type -TypeDefinition @'
{{LoaderSource}}
'@ -Language CSharp -ErrorAction Stop
                }
                $PowerForgeDevelopmentModuleAssembly = [{{LoaderTypeName}}]::LoadModule($PowerForgeDevelopmentBinaryPath, '{{DevelopmentContextName}}')
                $PowerForgeDevelopmentInnerModule = & $ImportModule -Assembly $PowerForgeDevelopmentModuleAssembly -Force -PassThru -ErrorAction Stop
{{TypeAcceleratorSetupBlock}}
                if ($PowerForgeDevelopmentInnerModule) {
{{ExportBridgeBlock}}
                }
