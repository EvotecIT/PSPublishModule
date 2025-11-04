$Parser = [System.Management.Automation.Language.Parser]::ParseInput($Content, [ref]$null, [ref]$null)
#$variables = $Parser.FindAll({$args[0].GetType().Name -like 'VariableExpressionAst'}, $true).Where({$_.VariablePath.UserPath -ne '_'})
$Variables = $Parser.FindAll({ $args[0].GetType().Name -like 'ExpandableStringExpressionAst ' }, $true).Where({ $_.VariablePath.UserPath -ne '_' })
#$variables
foreach ($Var in $Variables) {
    if ($Var.Extent.Text -eq '$PSScriptRoot') {
        $Var
    }
}
$parameters = $Parser.FindAll({ $args[0].GetType().Name -like 'ParameterAst' }, $true)

{
    "$a$b"
    "c$d"
}.Ast.FindAll({ $args[0] -is [System.Management.Automation.Language.ExpandableStringExpressionAst] }, 1) | ForEach-Object {
    $varExprCount = $_.FindAll({ $args[0] -is [System.Management.Automation.Language.VariableExpressionAst] }, 1).Count
    Write-Host "$_ has $varExprCount variable expressions embedded"
}


$Test = [io.path]::Combine($PSScriptRoot, '..', 'Images')
$Test