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
if ($PSEdition -eq 'Core' -and $HasNamedCorePayload -and ($Framework -eq 'Default' -or [string]::IsNullOrWhiteSpace($Framework))) {
    Write-Error -Message 'No compatible PowerShell Core assemblies found'
    return
}

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

    throw "Configured binary module '$LibraryFileName' was not found in a compatible Lib layout."
}
