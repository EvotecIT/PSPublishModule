Import-Module .\PSPublishModule.psd1 -Force

$removeCommentsSplat = @{
    SourceFilePath      = 'C:\Users\przemyslaw.klys\OneDrive - Evotec Services sp. z o.o\Documents\PowerShell\Modules\PSWriteHTML\PSWriteHTML.psm1'
    DestinationFilePath = 'C:\Support\GitHub\PSPublishModule\Examples\TestScript1.ps1'
}

#Remove-Comments @removeCommentsSplat -RemoveCommentsInParamBlock -RemoveAllEmptyLines -RemoveCommentsBeforeParamBlock

$removeCommentsSplat = @{
    SourceFilePath                 = 'C:\Support\GitHub\PSPublishModule\Examples\TestScript.ps1'
    DestinationFilePath            = 'C:\Support\GitHub\PSPublishModule\Examples\TestScript1.ps1'
    RemoveAllEmptyLines            = $true
    RemoveCommentsInParamBlock     = $true
    RemoveCommentsBeforeParamBlock = $true
}

Remove-Comments @removeCommentsSplat