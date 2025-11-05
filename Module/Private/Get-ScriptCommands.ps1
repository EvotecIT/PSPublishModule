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
        $FileAst = [System.Management.Automation.Language.Parser]::ParseFile($FilePath, [ref]$astTokens, [ref]$astErr)
    } else {
        $FileAst = [System.Management.Automation.Language.Parser]::ParseInput($Code, [ref]$astTokens, [ref]$astErr)
    }
    #$Commands = [System.Collections.Generic.List[Object]]::new()

    $Commands = Get-AstTokens -ASTTokens $astTokens -Commands $Commands -FileAst $FileAst
    #if ($CommandsOnly) {
    $Commands | Sort-Object -Unique
    # } else {
    #    $Commands
    # }
    # $astTokens | Group-Object tokenflags -AsHashTable -AsString
    #$Commands = $astTokens | Where-Object { $_.TokenFlags -eq 'Command' } | Sort-Object -Property Value -Unique
}