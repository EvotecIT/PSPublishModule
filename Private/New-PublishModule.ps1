function New-PublishModule {
    [cmdletbinding()]
    param(
        $projectName,
        $apikey,
        [bool] $RequireForce
    )
    Publish-Module -Name $projectName -Repository PSGallery -NuGetApiKey $apikey -Force:$RequireForce -verbose
}