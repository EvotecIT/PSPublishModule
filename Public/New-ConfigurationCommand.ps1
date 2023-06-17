function New-ConfigurationCommand {
    [CmdletBinding()]
    param(
        [string] $ModuleName,
        [string[]] $CommandName
    )

    $Configuration = [ordered] @{
        Type          = 'Command'
        Configuration = [ordered] @{
            ModuleName  = $ModuleName
            CommandName = $CommandName
        }
    }
    $Configuration
}