$LibraryFileNames = {{LibraryFileNames}}
$LibRoot = [IO.Path]::Combine($PowerForgeModuleRoot, 'Lib')

$ResolvePowerForgeModuleAssembly = {
    param([Parameter(Mandatory = $true)][string] $LibraryFileName)

    $Locations = @{}
    foreach ($Folder in @('Standard', 'Core', 'Default', '')) {
        $Directory = if ([string]::IsNullOrWhiteSpace($Folder)) {
            $LibRoot
        } else {
            [IO.Path]::Combine($LibRoot, $Folder)
        }
        if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
            continue
        }

        $Match = @(Get-ChildItem -LiteralPath $Directory -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ieq $LibraryFileName } |
            Select-Object -First 1)[0]
        if ($null -ne $Match) {
            $Locations[$Folder] = $Match.FullName
        }
    }

    $PreferredFolders = if ($PSEdition -eq 'Core') {
        @('Standard', 'Core', '', 'Default')
    } else {
        @('Default', 'Standard', '', 'Core')
    }
    foreach ($Folder in $PreferredFolders) {
        if ($Locations.ContainsKey($Folder)) {
            return [pscustomobject]@{
                Path = $Locations[$Folder]
                Folder = $Folder
                Directory = [IO.Path]::GetDirectoryName($Locations[$Folder])
            }
        }
    }

    throw "Configured binary module '$LibraryFileName' was not found in a compatible Lib layout."
}
