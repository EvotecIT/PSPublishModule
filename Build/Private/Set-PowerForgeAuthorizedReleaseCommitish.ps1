function Set-PowerForgeAuthorizedReleaseCommitish {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [psobject] $ReleaseConfig,

        [Parameter(Mandatory)]
        [ValidateSet('Plan', 'Prepare', 'Publish')]
        [string] $Operation,

        [Parameter(Mandatory)]
        [ValidatePattern('^[0-9a-fA-F]{40}$')]
        [string] $ExpectedCommit
    )

    if ($null -eq $ReleaseConfig.GitHub) {
        throw 'The release configuration does not declare a GitHub section.'
    }

    $commitishProperty = $ReleaseConfig.GitHub.PSObject.Properties['Commitish']
    if ($Operation -eq 'Prepare') {
        if ($null -ne $commitishProperty) {
            $ReleaseConfig.GitHub.PSObject.Properties.Remove('Commitish')
        }
    } else {
        $ReleaseConfig.GitHub |
            Add-Member -NotePropertyName Commitish -NotePropertyValue $ExpectedCommit -Force
    }

    $ReleaseConfig
}
