function Get-DirectoryFunctionOwnership {
    param([string] $Surface, [string] $ImportPath, [string] $ResultPath, [object[]] $OwnedFunctions)

    if (@($OwnedFunctions.name | Sort-Object -Unique).Count -ne $OwnedFunctions.Count) {
        throw 'The directory observer requires one owned declaration per function name.'
    }
    $moduleBase = Split-Path -LiteralPath $ImportPath
    $frames = [ordered]@{}
    if ($Surface -eq 'Original') {
        foreach ($function in $OwnedFunctions) {
            $frames[$function.name] = [IO.Path]::GetFullPath((Join-Path $moduleBase $function.relativePath))
        }
        return @{ frames = $frames; assembly = $null }
    }

    $receipt = Join-Path (Split-Path -LiteralPath $ResultPath) 'build-receipt.json'
    $manifest = (Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json).manifest
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($ImportPath) + '.dll'
    $assembly = Join-Path $moduleBase $assemblyName
    $files = @($manifest.files | Where-Object { [IO.Path]::GetFileName($_.path) -ceq $assemblyName })
    if ($files.Count -ne 1 -or (Get-FileHash -LiteralPath $assembly).Hash -ine $files[0].sha256) {
        throw 'The observed CLR assembly did not match the canonical build receipt.'
    }

    $ledger = @{}
    foreach ($entry in $manifest.unitDispositionLedger.entries) {
        if ($entry.kind -eq 'Function' -and $entry.emittedClrMethod -and !$entry.retainedHostedSource) {
            $ledger[$entry.unitId] = $entry
        }
    }
    foreach ($location in $manifest.failureMap.entries) {
        $unit = $ledger[$location.unitId]
        if ($unit -and $unit.generatedMemberName -ceq $location.generatedMemberName -and
            $unit.name -ceq $location.unitName -and $unit.relativePath -ceq $location.relativePath) {
            $identity = $location.generatedDeclaringTypeName + '::' + $location.generatedMemberName.TrimStart('@')
            if ($frames.Contains($identity) -and $frames[$identity] -cne $unit.name) {
                throw 'The build receipt identifies multiple authored functions for one CLR frame.'
            }
            $frames[$identity] = $unit.name
        }
    }
    if (@($frames.Values | Sort-Object -Unique).Count -ne $OwnedFunctions.Count) {
        throw 'The build receipt does not bind every owned function to an emitted CLR method.'
    }
    return @{ frames = $frames; assembly = $assembly }
}

function New-DirectoryFunctionObserver {
    param([string] $Surface, [string] $ModuleBase, $Ownership, $Entered)

    $owners = $Ownership.frames
    $assemblyPath = $Ownership.assembly
    $debugger = [Management.Automation.Runspaces.Runspace]::DefaultRunspace.Debugger
    $action = if ($Surface -eq 'Original') {
        {
            param($name, $lookup)
            foreach ($frame in $debugger.GetCallStack()) {
                $command = $frame.InvocationInfo.MyCommand
                if ($command -is [Management.Automation.FunctionInfo] -and $command.Module.ModuleBase -ieq $ModuleBase -and
                    $owners.Contains($command.Name) -and $command.ScriptBlock.File -ieq $owners[$command.Name] -and
                    $frame.FunctionName -ne '<ScriptBlock>') {
                    [void] $Entered.Add($command.Name)
                }
            }
        }
    } else {
        {
            param($name, $lookup)
            foreach ($frame in [Diagnostics.StackTrace]::new().GetFrames()) {
                $method = $frame.GetMethod()
                $type = $method.DeclaringType
                if ($type -and $type.Assembly.Location -ieq $assemblyPath) {
                    $identity = $type.FullName + '::' + $method.Name
                    if ($owners.Contains($identity)) { [void] $Entered.Add($owners[$identity]) }
                }
            }
        }
    }
    return $action.GetNewClosure()
}
