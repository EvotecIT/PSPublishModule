function Get-ModuleTestGreeting {
    <#
    .SYNOPSIS
    Returns a greeting for the module-test workflow sample.
    .PARAMETER Name
    Person to greet.
    .EXAMPLE
    Get-ModuleTestGreeting -Name 'World'
    #>
    [CmdletBinding()]
    param([string]$Name = 'World')

    "Hello, $Name!"
}
