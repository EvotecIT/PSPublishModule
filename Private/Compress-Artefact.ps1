function Compress-Artefact {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $Source,
        [string] $Destination,
        [string] $ModuleName,
        [string] $ModuleVersion,
        [nullable[bool]] $IncludeTag,
        [nullable[bool]] $LegacyName,
        [string] $ArtefactName,
        [string] $ID
    )
    if ($LegacyName) {
        # This is to support same, old configuration and not break existing projects
        $FileName = -join ("v$($ModuleVersion)", '.zip')
    } elseif ($ArtefactName) {
        $TagName = "v$($ModuleVersion)"
        $FileName = $ArtefactName
        $FileName = $FileName.Replace('{ModuleName}', $ModuleName)
        $FileName = $FileName.Replace('<ModuleName>', $ModuleName)
        $FileName = $FileName.Replace('{ModuleVersion}', $ModuleVersion)
        $FileName = $FileName.Replace('<ModuleVersion>', $ModuleVersion)
        $FileName = $FileName.Replace('{TagName}', $TagName)
        $FileName = $FileName.Replace('<TagName>', $TagName)
        # if user specified a file extension, we don't want to add .zip extension
        $FileName = if ($FileName.EndsWith(".zip")) { $FileName } else { -join ($FileName, '.zip') }
    } else {
        if ($IncludeTag) {
            $TagName = "v$($ModuleVersion)"
        } else {
            $TagName = ''
        }
        if ($TagName) {
            $FileName = -join ($ModuleName, ".$TagName", '.zip')
        } else {
            $FileName = -join ($ModuleName, '.zip')
        }
    }
    $ZipPath = [System.IO.Path]::Combine($Destination, $FileName)
    $ZipPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ZipPath)

    $Configuration.CurrentSettings.ArtefactZipName = $FileName
    $Configuration.CurrentSettings.ArtefactZipPath = $ZipPath

    if ($ID) {
        # ID was provided
        $Configuration.CurrentSettings['Artefact'][$ID] = [ordered] @{
            'ZipName' = $FileName
            'ZipPath' = $ZipPath
        }
    } else {
        if (-not $Configuration.CurrentSettings['ArtefactDefault']) {
            $Configuration.CurrentSettings['ArtefactDefault'] = [ordered] @{
                'ZipName' = $FileName
                'ZipPath' = $ZipPath
            }
        }
    }

    $Success = Write-TextWithTime -Text "Compressing final merged release $ZipPath" {
        $null = New-Item -ItemType Directory -Path $Destination -Force
        # Keep in mind we're skipping hidden files, as compress-archive doesn't support those
        # and I don't feel like rewritting it myself :-)
        # but i believe we should not be copying them in the first place
        # from what I saw most are PowerShellGet cache files
        [Array] $DirectoryToCompress = Get-ChildItem -Path $Source -Directory -ErrorAction SilentlyContinue
        [Array] $FilesToCompress = Get-ChildItem -Path $Source -File -Exclude '*.zip' -ErrorAction SilentlyContinue
        if ($DirectoryToCompress.Count -gt 0 -and $FilesToCompress.Count -gt 0) {
            Compress-Archive -Path @($DirectoryToCompress.FullName + $FilesToCompress.FullName) -DestinationPath $ZipPath -Force -ErrorAction Stop
        } elseif ($DirectoryToCompress.Count -gt 0) {
            Compress-Archive -Path $DirectoryToCompress.FullName -DestinationPath $ZipPath -Force -ErrorAction Stop
        } elseif ($FilesToCompress.Count -gt 0) {
            Compress-Archive -Path $FilesToCompress.FullName -DestinationPath $ZipPath -Force -ErrorAction Stop
        }
    } -PreAppend 'Plus' -SpacesBefore '      '

    if ($Success -eq $false) {
        return $false
    }
}