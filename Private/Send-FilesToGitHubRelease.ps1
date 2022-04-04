function Send-FilesToGitHubRelease([string[]] $filePathsToUpload, [string] $urlToUploadFilesTo, $authHeader) {
    [int] $numberOfFilesToUpload = $filePathsToUpload.Count
    [int] $numberOfFilesUploaded = 0
    $filePathsToUpload | ForEach-Object `
    {
        $filePath = $_
        $fileName = Get-Item $filePath | Select-Object -ExpandProperty Name

        $uploadAssetWebRequestParameters =
        @{
            # Append the name of the file to the upload url.
            Uri         = $urlToUploadFilesTo + "?name=$fileName"
            Method      = 'POST'
            Headers     = $authHeader
            ContentType = 'application/zip'
            InFile      = $filePath
        }

        $numberOfFilesUploaded = $numberOfFilesUploaded + 1
        Write-Verbose "Uploading asset $numberOfFilesUploaded of $numberOfFilesToUpload, '$filePath'."
        Invoke-RestMethodAndThrowDescriptiveErrorOnFailure $uploadAssetWebRequestParameters > $null
    }
}
