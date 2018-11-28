function Export-PSData
{
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

    param(
    # The data that will be exported
    [Parameter(Mandatory=$true,ValueFromPipeline=$true)]
    [PSObject[]]
    $InputObject,

    # The path to the data file
    [Parameter(Mandatory=$true,Position=0)]
    [string]
    $DataFile
    )
    begin {
        $AllObjects = New-Object Collections.ArrayList
    }

    process {
        $null = $AllObjects.AddRange($InputObject)
    }

    end {
        #region Convert to Hashtables and export
        $text = $AllObjects |
            Write-PowerShellHashtable

        $text |
            Set-Content -Path $DataFile
        Get-Item -Path $DataFile
        #endregion Convert to Hashtables and export
    }

}