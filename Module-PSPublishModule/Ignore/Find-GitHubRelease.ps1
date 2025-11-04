function Find-GitHubRelease {
    [CmdletBinding()]
    param(
        [string] $Repository
    )
    $url = "$Repository/releases/latest"
    $request = [System.Net.WebRequest]::Create($url)
    $response = $request.GetResponse()
    $realTagUrl = $response.ResponseUri.OriginalString
    $version = $realTagUrl.split('/')[-1].Trim('v')
    $version

    #$fileName = "PowerShell-$version-win-x64.zip"
    #$realDownloadUrl = $realTagUrl.Replace('tag', 'download') + '/' + $fileName
    #$realDownloadUrl
    #https://github.com/PowerShell/PowerShell/releases/download/v6.2.3/PowerShell-6.2.3-win-x64.zip
     #Invoke-WebRequest -Uri $realDownloadUrl -OutFile $env:TEMP/$fileName
}

Find-GitHubRelease -Repository 'https://github.com/EvotecIT/PSWriteHTML'