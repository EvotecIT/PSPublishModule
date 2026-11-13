    # A failed import never reaches OnRemove, so release the accelerators it registered here.
    if (-not $PowerForgeModuleImportCompleted -and $null -ne $script:PowerForgeTypeAcceleratorRegistrations) {
        foreach ($PowerForgeTypeAcceleratorOwner in $script:PowerForgeTypeAcceleratorRegistrations) {
            $PowerForgeTypeAcceleratorOwner.Unregister()
        }
        $script:PowerForgeTypeAcceleratorRegistrations.Clear()
    }
