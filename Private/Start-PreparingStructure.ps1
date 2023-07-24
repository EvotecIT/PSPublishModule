function Start-PreparingStructure {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [System.Collections.IDictionary] $DestinationPaths,
        [string] $FullProjectPath,
        [string] $FullModuleTemporaryPath,
        [string] $FullTemporaryPath
    )
    Write-TextWithTime -Text "Preparing structure" -PreAppend Information {
        if ($Configuration.Steps.BuildModule.DeleteBefore -eq $true) {
            Write-TextWithTime -Text "Deleting old module (Desktop destination) $($DestinationPaths.Desktop)" {
                $Success = Remove-Directory -Directory $($DestinationPaths.Desktop) -ErrorAction Stop
                if ($Success -eq $false) {
                    return $false
                }
            } -PreAppend Minus -SpacesBefore "   " -Color Blue -ColorError Red -ColorTime Green -ColorBefore Yellow

            Write-TextWithTime -Text "Deleting old module (Core destination) $($DestinationPaths.Core)" {
                $Success = Remove-Directory -Directory $($DestinationPaths.Core)
                if ($Success -eq $false) {
                    return $false
                }
            } -PreAppend Minus -SpacesBefore "   " -Color Blue -ColorError Red -ColorTime Green -ColorBefore Yellow
        }

        Set-Location -Path $FullProjectPath

        Write-TextWithTime -Text "Cleaning up temporary path $($FullModuleTemporaryPath)" {
            $Success = Remove-Directory -Directory $FullModuleTemporaryPath
            if ($Success -eq $false) {
                return $false
            }
            Add-Directory -Directory $FullModuleTemporaryPath
        } -PreAppend Minus -SpacesBefore "   " -Color Blue -ColorError Red -ColorTime Green -ColorBefore Yellow
        Write-TextWithTime -Text "Cleaning up temporary path $($FullTemporaryPath)" {
            $Success = Remove-Directory -Directory $FullTemporaryPath
            if ($Success -eq $false) {
                return $false
            }
            Add-Directory -Directory $FullTemporaryPath
        } -PreAppend Minus -SpacesBefore "   " -Color Blue -ColorError Red -ColorTime Green -ColorBefore Yellow
    }
}