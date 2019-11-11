<#
function Get-ScriptCommands {
    param(
        [string] $Path
    )
    New-Variable astTokens -force
    New-Variable astErr -force

    $null = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$astTokens, [ref]$astErr)

    $Commands = [System.Collections.Generic.List[Object]]::new()

    foreach ($_ in $astTokens) {
        if ($_.TokenFlags -eq 'Command' -and $_.Kind -eq 'Generic') {
            if ($_.Value -notin $Commands) {
                $Commands.Add($_.Value)
            }
        }
    }
    $Commands | Sort-Object
    # $astTokens | Group-Object tokenflags -AsHashTable -AsString
    #$Commands = $astTokens | Where-Object { $_.TokenFlags -eq 'Command' } | Sort-Object -Property Value -Unique
}
#>
function Get-ScriptCommands {
    [cmdletBinding(DefaultParameterSetName = 'File')]
    param (
        [alias('Path')][Parameter(ParameterSetName = 'File')][string] $FilePath,
        [alias('ScriptBlock')][scriptblock] $Code,
        [switch] $CommandsOnly
    )
    begin {
        $Errors = $null
    }
    process {
        $Errors = $null
        if ($Code) {
            $CodeRead = $Code
        } else {
            if (Test-Path -LiteralPath $FilePath) {
                $CodeRead = Get-Content -Path $FilePath -Raw -Encoding Default
            }
        }
        if ($CodeRead) {
            $Tokens = [System.Management.Automation.PSParser]::Tokenize($CodeRead, [ref]$Errors)
            $Commands = foreach ($_ in $Tokens) {
                if ($_.Type -eq 'Command') {
                    $_
                }
            }
            if ($CommandsOnly) {
                $Commands.Content | Sort-Object -Unique
            } else {
                $Commands
            }
        }
    }
}