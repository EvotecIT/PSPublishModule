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
        [string] $ArtefactName
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

    $Success = Write-TextWithTime -Text "Compressing final merged release $ZipPath" {
        $null = New-Item -ItemType Directory -Path $Destination -Force
        Compress-Archive -Path $CompressPath -DestinationPath $ZipPath -Force -ErrorAction Stop
    } -PreAppend 'Plus'

    if ($Success -eq $false) {
        return $false
    }
}