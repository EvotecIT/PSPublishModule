function New-ConfigurationArtefact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Unpacked', 'Packed')][string] $Type,
        [switch] $Enable,
        [switch] $IncludeTagName,
        [string] $Path,
        [switch] $RequiredModules,
        [System.Collections.IDictionary] $CopyDirectories,
        [System.Collections.IDictionary] $CopyFiles
    )

    if ($Type -eq 'Packed') {
        $ArtefactType = 'Releases'
    } else {
        $ArtefactType = 'ReleasesUnpacked'
    }

    # $BuildModule = @{  # requires Enable to be on to process all of that
    #     #CreateFileCatalog       = $false
    #     Releases         = $true
    #     #ReleasesUnpacked        = $false
    #     ReleasesUnpacked = @{
    #         Enabled         = $false
    #         IncludeTagName  = $false
    #         Path            = "$PSScriptRoot\..\Artefacts"
    #         RequiredModules = $false
    #         DirectoryOutput = @{

    #         }
    #         FilesOutput     = @{

    #         }
    #     }
    # }

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
    if ($PSBoundParameters.ContainsKey('RequiredModules')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                RequiredModules = $RequiredModules
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
    if ($PSBoundParameters.ContainsKey('CopyFiles')) {
        [ordered] @{
            Type          = $ArtefactType
            $ArtefactType = [ordered] @{
                FilesOutput = $CopyFiles
            }
        }
    }
}