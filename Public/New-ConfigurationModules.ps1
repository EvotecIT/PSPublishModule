function New-ConfigurationModules {
    [CmdletBinding()]
    param(
        [validateset('RequiredModule', 'ExternalModule')] $Type = 'RequiredModule',
        [string] $Name,
        [string] $Version,
        [string] $Guid
    )
    $ModuleInformation = [ordered] @{
        ModuleName    = $Name
        ModuleVersion = $Version
        Guid          = $Guid
    }
    Remove-EmptyValue -Hashtable $ModuleInformation
    if ($ModuleInformation.Count -eq 0) {
        return
    } elseif ($ModuleInformation.Count -eq 1 -and $ModuleInformation.Contains('ModuleName')) {
        $Configuration = $Name
    } else {
        $Configuration = $ModuleInformation
    }
    $Option = @{
        Type          = $Type
        Configuration = $Configuration
    }
    $Option
}