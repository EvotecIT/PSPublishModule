function New-ConfigurationArtefact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Unpacked', 'Packed')][string] $Type,
        [switch] $Enable,
        [switch] $IncludeTagName,
        [string] $Path,
        [alias('RequiredModules')][switch] $AddRequiredModules,
        [string] $ModulesPath,
        [string] $RequiredModulesPath,
        [System.Collections.IDictionary] $CopyDirectories,
        [System.Collections.IDictionary] $CopyFiles,
        [switch] $CopyDirectoriesRelative,
        [switch] $CopyFilesRelative,
        [switch] $Clear,
        [string] $ArtefactName
    )

    if ($Type -eq 'Packed') {
        $ArtefactType = 'Releases'
    } else {
        $ArtefactType = 'ReleasesUnpacked'
    }

    if ($PSBoundParameters.ContainsKey('Enable')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                Enabled = $Enable
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('IncludeTagName')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                IncludeTagName = $IncludeTagName
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('Path')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                Path = $Path
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('RequiredModulesPath')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                RequiredModules = @{
                    Path = $RequiredModulesPath
                }
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('AddRequiredModules')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                RequiredModules = @{
                    Enabled = $true
                }
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('ModulesPath')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                RequiredModules = @{
                    ModulesPath = $ModulesPath
                }
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('CopyDirectories')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                DirectoryOutput = $CopyDirectories
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('CopyDirectoriesRelative')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                DestinationDirectoriesRelative = $CopyDirectoriesRelative.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('CopyFiles')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                FilesOutput = $CopyFiles
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('CopyFilesRelative')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                DestinationFilesRelative = $CopyFilesRelative.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('Clear')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                Clear = $Clear
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('ArtefactName')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                ArtefactName = $ArtefactName
            }
        }
    }
}