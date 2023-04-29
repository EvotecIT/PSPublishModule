function Copy-ArtefactMainModule {
    [CmdletBinding()]
    param(
        [switch] $Enabled,
        [string] $IncludeTagName,
        [string] $ModuleName,
        [string] $Destination,
        [string] $CurrentModulePath
    )
    if (-not $Enabled) {
        return
    }
    if ($IncludeTagName) {
        $NameOfDestination = [io.path]::Combine($CurrentModulePath, $ModuleName, $TagName)
    } else {
        $NameOfDestination = [io.path]::Combine($CurrentModulePath, $ModuleName)
    }
    Write-TextWithTime -PreAppend Addition -Text "Copying main module to $NameOfDestination" -Color Yellow {
        #Write-Text "   [>] Copying main module to $NameOfDestination" -Color Yellow
        # try {
        if (Test-Path -Path $NameOfDestination) {
            Remove-ItemAlternative -LiteralPath $NameOfDestination #-SkipFolder
        }
        $null = New-Item -ItemType Directory -Path $Destination -Force

        if ($DestinationPaths.Desktop) {
            Copy-Item -LiteralPath $DestinationPaths.Desktop -Recurse -Destination $NameOfDestination -Force
        } elseif ($DestinationPaths.Core) {
            Copy-Item -LiteralPath $DestinationPaths.Core -Recurse -Destination $NameOfDestination -Force
        }
        # } catch {
        #     $ErrorMessage = $_.Exception.Message
        #     Write-Host # This is to add new line, because the first line was opened up.
        #     Write-Text "[-] Start-ArtefactsBuilding - Copying final merged release to $Destination failed. Error: $ErrorMessage" -Color Red
        #     return $false
        # }
    } -SpacesBefore '   '
}