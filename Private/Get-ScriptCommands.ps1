function Get-ScriptCommands {
    [CmdletBinding()]
    param(
        [string] $FilePath,
        [alias('ScriptBlock')][scriptblock] $Code,
        [switch] $CommandsOnly
    )
    $astTokens = $null
    $astErr = $null

    if ($FilePath) {
        $null = [System.Management.Automation.Language.Parser]::ParseFile($FilePath, [ref]$astTokens, [ref]$astErr)
    } else {
        $null = [System.Management.Automation.Language.Parser]::ParseInput($Code, [ref]$astTokens, [ref]$astErr)
    }
    $Commands = [System.Collections.Generic.List[Object]]::new()
    Get-AstTokens -ASTTokens $astTokens -Commands $Commands
    if ($CommandsOnly) {
        $Commands.Value | Sort-Object -Unique
    } else {
        $Commands
    }
    # $astTokens | Group-Object tokenflags -AsHashTable -AsString
    #$Commands = $astTokens | Where-Object { $_.TokenFlags -eq 'Command' } | Sort-Object -Property Value -Unique
}

function Get-AstTokens {
    [cmdletBinding()]
    param(
        [System.Management.Automation.Language.Token[]] $ASTTokens,
        [System.Collections.Generic.List[Object]] $Commands
    )
    foreach ($_ in $astTokens) {
        if ($_.TokenFlags -eq 'Command' -and $_.Kind -eq 'Generic') {
            if ($_.Value -notin $Commands) {
                $Commands.Add($_)
            }
        } else {
            if ($_.NestedTokens) {
                Get-AstTokens -ASTTokens $_.NestedTokens -Commands $Commands
            }
        }
    }
}

function Get-ScriptCommandsUseless {
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
            if ($FilePath -eq '') {
                Write-Text "[-] Something went wrong. FilePath for Get-ScriptCommands was empty. Rerun tool to verify." -Color Red
            } else {
                if (Test-Path -LiteralPath $FilePath) {
                    $CodeRead = Get-Content -Path $FilePath -Raw -Encoding Default
                }
            }
        }
        if ($CodeRead) {
            #$Types = [System.Collections.Generic.List[string]]::new()
            $Tokens = [System.Management.Automation.PSParser]::Tokenize($CodeRead, [ref]$Errors)
            $Commands = foreach ($_ in $Tokens) {
                if ($_.Type -eq 'Command') {
                    $_
                }
                #else {
                #  $Types.Add($_.Type)
                #}
            }
            if ($CommandsOnly) {
                $Commands.Content | Sort-Object -Unique
            } else {
                $Commands
            }
        }
    }
}

