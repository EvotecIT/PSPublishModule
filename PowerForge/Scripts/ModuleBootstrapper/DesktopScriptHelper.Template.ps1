# Script modules may carry a Core-only helper without exporting binary cmdlets or types.
# Keep their Desktop functions available without importing the incompatible helper.
if ($PSEdition -ne 'Core' -and ($Core -or $HasNamedCorePayload) -and -not $Default -and -not $Standard) {
    $PowerForgeHasDesktopPayload = @(Get-ChildItem -LiteralPath $LibRoot -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            if ($_.Extension -ine '.dll') { return $false }
            $RelativeCandidate = $_.FullName.Substring($LibRoot.Length).TrimStart([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
            $TopLevelFolder = @($RelativeCandidate -split '[\\/]')[0]
            $TopLevelFolder -notmatch '^Core(?:-(?:net|netcoreapp)\d+\.\d+)?$'
        }).Count -gt 0
    if (-not $PowerForgeHasDesktopPayload) {
        $LibraryFileNames = @()
    }
}
