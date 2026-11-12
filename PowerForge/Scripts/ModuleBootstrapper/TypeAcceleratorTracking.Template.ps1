foreach ($PowerForgeTypeAcceleratorMessage in $PowerForgeTypeAcceleratorRegistration.Warnings) {
    Write-Warning -Message $PowerForgeTypeAcceleratorMessage
}
foreach ($PowerForgeTypeAcceleratorMessage in $PowerForgeTypeAcceleratorRegistration.VerboseMessages) {
    Write-Verbose -Message $PowerForgeTypeAcceleratorMessage
}
if ($null -eq $script:PowerForgeTypeAcceleratorRegistrations) {
    $script:PowerForgeTypeAcceleratorRegistrations = [System.Collections.Generic.List[object]]::new()
    $PowerForgeTypeAcceleratorRegistrations = $script:PowerForgeTypeAcceleratorRegistrations
    if ($null -ne $ExecutionContext.SessionState.Module) {
        # Remove-Module releases this module's accelerators; names another PowerForge module still owns stay.
        $PreviousPowerForgeOnRemove = $ExecutionContext.SessionState.Module.OnRemove
        $ExecutionContext.SessionState.Module.OnRemove = {
            try {
                foreach ($PowerForgeTypeAcceleratorOwner in $PowerForgeTypeAcceleratorRegistrations) {
                    $PowerForgeTypeAcceleratorOwner.Unregister()
                }
            } finally {
                if ($null -ne $PreviousPowerForgeOnRemove) {
                    & $PreviousPowerForgeOnRemove @args
                }
            }
        }.GetNewClosure()
    }
}
$script:PowerForgeTypeAcceleratorRegistrations.Add($PowerForgeTypeAcceleratorRegistration)
