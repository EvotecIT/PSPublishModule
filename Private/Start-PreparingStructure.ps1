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
            Write-Text "   [-] Deleting old module (Desktop destination) $($DestinationPaths.Desktop)" -Color Yellow
            $Success = Remove-Directory -Directory $($DestinationPaths.Desktop)
            if ($Success -eq $false) {
                return $false
            }
            Write-Text "   [-] Deleting old module (Core destination) $($DestinationPaths.Core)" -Color Yellow
            $Success = Remove-Directory -Directory $($DestinationPaths.Core)
            if ($Success -eq $false) {
                return $false
            }
        }

        Set-Location -Path $FullProjectPath

        Write-Text "   [-] Cleaning up temporary path $($FullModuleTemporaryPath)" -Color Yellow
        $Success = Remove-Directory -Directory $FullModuleTemporaryPath
        if ($Success -eq $false) {
            return $false
        }
        Write-Text "   [-] Cleaning up temporary path $($FullTemporaryPath)" -Color Yellow
        $Success = Remove-Directory -Directory $FullTemporaryPath
        if ($Success -eq $false) {
            return $false
        }
        Add-Directory -Directory $FullModuleTemporaryPath
        Add-Directory -Directory $FullTemporaryPath
    }
}