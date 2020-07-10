function Step-Version {
    [cmdletBinding()]
    param(
        [string] $Module,
        [ValidateSet('Major', 'Minor', 'Build', 'Revision')][string] $Update = 'Build'
    )
    $ModuleGallery = Find-Module -Name $Module
    [version] $CurrentVersion = [version] $ModuleGallery.Version
    $Types = @('Major', 'Minor', 'Build', 'Revision')
    $NewVersion = foreach ($Type in $Types) {
        if ($Type -eq $Update) {
            $CurrentVersion.$Update + 1
        } else {
            if ($CurrentVersion.$Type -ne -1) {
                $CurrentVersion.$Type
            }
        }
    }
    $NewVersion -join '.'
}
#Step-Version -Module Testimo -Update Minor