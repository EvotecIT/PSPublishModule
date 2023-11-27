function New-ConfigurationDocumentation {
    <#
    .SYNOPSIS
    Enables or disables creation of documentation from the module using PlatyPS

    .DESCRIPTION
    Enables or disables creation of documentation from the module using PlatyPS

    .PARAMETER Enable
    Enables creation of documentation from the module. If not specified, the documentation will not be created.

    .PARAMETER StartClean
    Removes all files from the documentation folder before creating new documentation.
    Otherwise the `Update-MarkdownHelpModule` will be used to update the documentation.

    .PARAMETER UpdateWhenNew
    Updates the documentation right after running `New-MarkdownHelp` due to platyPS bugs.

    .PARAMETER Path
    Path to the folder where documentation will be created.

    .PARAMETER PathReadme
    Path to the readme file that will be used for the documentation.

    .PARAMETER Tool
    Tool to use for documentation generation. By default `HelpOut` is used.
    Available options are `PlatyPS` and `HelpOut`.

    .EXAMPLE
    New-ConfigurationDocumentation -Enable:$false -StartClean -UpdateWhenNew -PathReadme 'Docs\Readme.md' -Path 'Docs'

    .EXAMPLE
    New-ConfigurationDocumentation -Enable -PathReadme 'Docs\Readme.md' -Path 'Docs'

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param(
        [switch] $Enable,
        [switch] $StartClean,
        [switch] $UpdateWhenNew,
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $PathReadme,
        [ValidateSet('PlatyPS', 'HelpOut')][string] $Tool = 'HelpOut'
    )

    if ($Path -or $PathReadme) {
        $Option = [ordered] @{
            Type          = 'Documentation'
            Configuration = [ordered] @{
                Path       = $Path
                PathReadme = $PathReadme
            }
        }
        $Option
    }

    if ($Enable -or $StartClean -or $UpdateWhenNew) {
        $Option = [ordered]@{
            Type          = 'BuildDocumentation'
            Configuration = [ordered]@{
                Enable        = $Enable
                StartClean    = $StartClean
                UpdateWhenNew = $UpdateWhenNew
                Tool          = $Tool
            }
        }
        $Option
    }
}