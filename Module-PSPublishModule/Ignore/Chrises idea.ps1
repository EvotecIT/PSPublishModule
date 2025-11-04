using namespace System.Management.Automation.Language
using namespace System.Collections.Generic

$module = "$pwd\Indented.Net.IP.psm1"

$ast = [Parser]::ParseFile($module, [ref]$null, [ref]$null)

[HashSet[string]]$declared = $ast.FindAll({ $args[0] -is [FunctionDefinitionAst] }, $false).Name
$used = $ast.FindAll({ $args[0] -is [CommandAst] -and -not $declared.Contains($args[0].GetCommandName()) }, $true) |
ForEach-Object GetCommandName

# idea 2


[HashSet[string]]$used = $ast.FindAll(
    { $args[0] -is [CommandAst] -and -not $declared.Contains($args[0].GetCommandName()) },
    $true
) | ForEach-Object GetCommandName

# idea 3


$ast = [Parser]::ParseFile($module, [ref]$null, [ref]$null)

# Functions I declare
$declared = $ast.FindAll( { $args[0] -is [FunctionDefinitionAst] }, $false) | ForEach-Object Name

# Commands I use
$used = $ast.FindAll( { $args[0] -is [CommandAst] }, $true) | ForEach-Object GetCommandName

# Filtered commands I use
$filtered = $used | Where-Object { $_ -notin $declared }

# Commands I use from bunch-o-approved modules
$approved = Get-Command -Module A, B, C
$filtered | Where-Object { $_ -in $approved.Name }