$LibraryFileNames = {{LibraryFileNames}}
$LibRoot = [IO.Path]::Combine($PowerForgeModuleRoot, 'Lib')
$AssemblyFolders = Get-ChildItem -LiteralPath $LibRoot -Directory -ErrorAction SilentlyContinue

$Default = $false
$Core = $false
$Standard = $false
$HasNamedCorePayload = $false
foreach ($AssemblyFolder in @($AssemblyFolders.Name)) {
    if ($AssemblyFolder -eq 'Default') {
        $Default = $true
    } elseif ($AssemblyFolder -eq 'Core') {
        $Core = $true
    } elseif ($AssemblyFolder -eq 'Standard') {
        $Standard = $true
    } elseif ($AssemblyFolder -match '^Core-(?:net|netcoreapp)\d+\.\d+$') {
        $HasNamedCorePayload = $true
    }
}

$Framework = if ($Standard) { 'Standard' } elseif ($Core) { 'Core' } else { '' }
$FrameworkNet = if ($Default) { 'Default' } elseif ($Standard) { 'Standard' } else { '' }
{{RuntimePayloadSelectorBlock}}
$PowerForgeHasNoCompatibleNamedCorePayload = $PSEdition -eq 'Core' -and $HasNamedCorePayload -and ($Framework -eq 'Default' -or [string]::IsNullOrWhiteSpace($Framework))

$PowerForgePreferredBinaryFolders = if ($PSEdition -eq 'Core') {
    @($Framework, 'Standard', 'Core', '', 'Default')
} else {
    @($FrameworkNet, 'Default', 'Standard', '', 'Core')
}
$PowerForgePreferredBinaryFolders = @($PowerForgePreferredBinaryFolders |
    Where-Object { $null -ne $_ } |
    Select-Object -Unique)

$ResolvePowerForgeModuleAssembly = {
    param([Parameter(Mandatory = $true)][string] $LibraryFileName)

    if ([IO.Path]::IsPathRooted($LibraryFileName)) {
        $AbsoluteMatch = Get-Item -LiteralPath $LibraryFileName -ErrorAction SilentlyContinue
        if ($null -ne $AbsoluteMatch -and -not $AbsoluteMatch.PSIsContainer) {
            return [pscustomobject]@{
                Path = $AbsoluteMatch.FullName
                Folder = $AbsoluteMatch.DirectoryName
                Directory = $AbsoluteMatch.DirectoryName
            }
        }
    } elseif ($LibraryFileName.IndexOfAny([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)) -ge 0) {
        $RelativeReference = $LibraryFileName.Replace([IO.Path]::AltDirectorySeparatorChar, [IO.Path]::DirectorySeparatorChar)
        $LibPrefix = 'Lib' + [IO.Path]::DirectorySeparatorChar
        if ($RelativeReference.StartsWith($LibPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            $RelativeReference = $RelativeReference.Substring($LibPrefix.Length)
        }

        $QualifiedMatch = @(Get-ChildItem -LiteralPath $LibRoot -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object {
                $RelativeCandidate = $_.FullName.Substring($LibRoot.Length).TrimStart([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
                $RelativeCandidate -ieq $RelativeReference
            } |
            Select-Object -First 1)[0]
        if ($null -ne $QualifiedMatch) {
            return [pscustomobject]@{
                Path = $QualifiedMatch.FullName
                Folder = [IO.Path]::GetDirectoryName($RelativeReference)
                Directory = [IO.Path]::GetDirectoryName($QualifiedMatch.FullName)
            }
        }
    }

    foreach ($Folder in $PowerForgePreferredBinaryFolders) {
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
            return [pscustomobject]@{
                Path = $Match.FullName
                Folder = $Folder
                Directory = [IO.Path]::GetDirectoryName($Match.FullName)
            }
        }
    }

    $RecursiveMatches = @(Get-ChildItem -LiteralPath $LibRoot -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            if ($_.Name -ine $LibraryFileName) { return $false }
            if (-not $PowerForgeHasNoCompatibleNamedCorePayload) { return $true }
            $RelativeCandidate = $_.FullName.Substring($LibRoot.Length).TrimStart([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
            $TopLevelFolder = @($RelativeCandidate -split '[\\/]')[0]
            $TopLevelFolder -notmatch '^Core-(?:net|netcoreapp)\d+\.\d+$'
        })
    if ($RecursiveMatches.Count -eq 1) {
        $RecursiveMatch = $RecursiveMatches[0]
        $RelativeDirectory = $RecursiveMatch.DirectoryName.Substring($LibRoot.Length).TrimStart([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
        return [pscustomobject]@{
            Path = $RecursiveMatch.FullName
            Folder = $RelativeDirectory
            Directory = $RecursiveMatch.DirectoryName
        }
    }
    if ($RecursiveMatches.Count -gt 1) {
        throw "Configured binary module '$LibraryFileName' matched multiple nested Lib payloads. Use a path-qualified ExportAssemblies entry."
    }
    if ($PowerForgeHasNoCompatibleNamedCorePayload) {
        throw 'No compatible PowerShell Core assemblies found'
    }

    throw "Configured binary module '$LibraryFileName' was not found in a compatible Lib layout."
}
