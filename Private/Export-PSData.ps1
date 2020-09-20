function Export-PSData {
    <#
    .Synopsis
        Exports property bags into a data file
    .Description
        Exports property bags and the first level of any other object into a ps data file (.psd1)
    .Link
        https://github.com/StartAutomating/Pipeworks
        Import-PSData
    .Example
        Get-Web -Url http://www.youtube.com/watch?v=xPRC3EDR_GU -AsMicrodata -ItemType http://schema.org/VideoObject |
            Export-PSData .\PipeworksQuickstart.video.psd1
    #>
    [OutputType([IO.FileInfo])]
    [cmdletbinding()]
    param(
        # The data that will be exported
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)][PSObject[]]$InputObject,
        # The path to the data file
        [Parameter(Mandatory = $true, Position = 0)][string] $DataFile,
        [switch] $Sort
    )
    begin {
        $AllObjects = [System.Collections.Generic.List[object]]::new()
    }
    process {
        $AllObjects.AddRange($InputObject)
    }
    end {
        #region Convert to Hashtables and export
        $Text = $AllObjects | Write-PowerShellHashtable -Sort:$Sort.IsPresent
        $Text | Out-File -FilePath $DataFile -Encoding UTF8
        #endregion Convert to Hashtables and export
    }

}