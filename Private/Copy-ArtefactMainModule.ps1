function Copy-ArtefactMainModule {
    [CmdletBinding()]
    param(
        [switch] $Enabled,
        [nullable[bool]] $IncludeTagName,
        [string] $ModuleName,
        [string] $Destination
    )
    if (-not $Enabled) {
        return
    }
    if ($IncludeTagName) {
        $NameOfDestination = [io.path]::Combine($Destination, $ModuleName, $TagName)
    } else {
        $NameOfDestination = [io.path]::Combine($Destination, $ModuleName)
    }
    $ResolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($NameOfDestination)
    Write-TextWithTime -PreAppend Addition -Text "Copying main module to $ResolvedDestination" -Color Yellow {
        if (Test-Path -Path $NameOfDestination) {
            Remove-ItemAlternative -LiteralPath $NameOfDestination
        }
        $null = New-Item -ItemType Directory -Path $Destination -Force

        if ($DestinationPaths.Desktop) {
            Copy-Item -LiteralPath $DestinationPaths.Desktop -Recurse -Destination $ResolvedDestination -Force
        } elseif ($DestinationPaths.Core) {
            Copy-Item -LiteralPath $DestinationPaths.Core -Recurse -Destination $ResolvedDestination -Force
        }
    } -SpacesBefore '      '
}