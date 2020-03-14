function New-PublishModule {
    [cmdletbinding()]
    param(
        [string] $projectName,
        [string] $apikey,
        [bool] $RequireForce
    )
    Publish-Module -Name $projectName -Repository PSGallery -NuGetApiKey $apikey -Force:$RequireForce -verbose
}