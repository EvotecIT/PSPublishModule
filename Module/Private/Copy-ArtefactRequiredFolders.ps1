function Copy-ArtefactRequiredFolders {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $FoldersInput,
        [string] $ProjectPath,
        [string] $Destination,
        [nullable[bool]] $DestinationRelative
    )
}