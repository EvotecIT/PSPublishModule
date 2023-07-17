function Copy-ArtefactRequiredFiles {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $FilesInput,
        [string] $ProjectPath,
        [string] $Destination,
        [nullable[bool]] $DestinationRelative
    )

    foreach ($File in $FilesInput.Keys) {
        if ($FilesInput[$File] -is [string]) {
            $FullFilePath = [System.IO.Path]::Combine($ProjectPath, $File)
            if (Test-Path -Path $FullFilePath) {
                if ($DestinationRelative) {
                    $DestinationPath = [System.IO.Path]::Combine($Destination, $FilesInput[$File])
                } else {
                    $DestinationPath = $FilesInput[$File]
                }
                $ResolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationPath)
                Write-TextWithTime -Text "Copying file $FullFilePath to $ResolvedDestination" {
                    $DirectoryPath = [Io.Path]::GetDirectoryName($ResolvedDestination)
                    $null = New-Item -ItemType Directory -Force -ErrorAction Stop -Path $DirectoryPath
                    Copy-Item -LiteralPath $FullFilePath -Destination $ResolvedDestination -Force -ErrorAction Stop
                } -PreAppend Plus -SpacesBefore "   " -Color Yellow
            } else {
                Write-TextWithTime -Text "File $FullFilePath does not exist" -PreAppend Plus -SpacesBefore "   " -Color Red -ColorTime Red -ColorBefore Red
                return $false
            }
        } elseif ($FilesInput[$File] -is [System.Collections.IDictionary]) {
            if ($FilesInput[$File].Enabled -eq $true) {
                if ($FilesInput[$File].Source) {
                    $FullFilePath = [System.IO.Path]::Combine($ProjectPath, $FilesInput[$File].Source)
                    if (Test-Path -Path $FullFilePath) {
                        if ($FilesInput[$File].DestinationRelative) {
                            $DestinationPath = [System.IO.Path]::Combine($Destination, $FilesInput[$File].Destination)
                        } else {
                            $DestinationPath = $FilesInput[$File].Destination
                        }
                        $ResolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationPath)
                        Write-TextWithTime -Text "Copying file $FullFilePath to $ResolvedDestination" {
                            $DirectoryPath = [Io.Path]::GetDirectoryName($ResolvedDestination)
                            $null = New-Item -ItemType Directory -Force -ErrorAction Stop -Path $DirectoryPath
                            Copy-Item -LiteralPath $FullFilePath -Destination $ResolvedDestination -Force -ErrorAction Stop
                        } -PreAppend Plus -SpacesBefore "   " -Color Yellow
                    } else {
                        Write-TextWithTime -Text "File $FullFilePath does not exist" -PreAppend Plus -SpacesBefore "   " -Color Red -ColorTime Red -ColorBefore Red
                        return $false
                    }
                }
            }
        }
    }
}