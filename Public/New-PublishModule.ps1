function New-PublishModule($projectName, $apikey) {
    Publish-Module -Name $projectName -Repository PSGallery -NuGetApiKey $apikey -verbose
}