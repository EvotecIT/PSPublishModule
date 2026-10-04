param([Parameter(Mandatory)][string]$Assembly)
$ErrorActionPreference='Stop'
Add-Type -Path $Assembly
$module = New-Module {
    function Invoke-Original {
        [CmdletBinding()] param([string]$Value)
        $Seen = [Collections.Generic.List[string]]::new()
        $Marker = 'outer'
        $Block = {
            param([string]$Text)
            $Seen.Add($Marker+':'+$Text)
            $Marker='child'
            $Marker
        }
        & $Block $Value
        & $Block 'again'
        $Marker
        $Seen -join '|'
    }
    Set-Item Function:\Invoke-Compiled -Value ([Generic.Compiler.StatementErrors.NativeScriptBlockFixture]::Create($ExecutionContext.SessionState.Module))
    Export-ModuleMember -Function Invoke-Original,Invoke-Compiled
}
Import-Module $module
try {
    [Generic.Compiler.StatementErrors.NativeScriptBlockFixture]::VerifyCallbackCompleteness($module)
    $isolated=[Generic.Compiler.StatementErrors.NativeScriptBlockFixture]::CreateIsolated($module)
    if ((& $isolated -Items @('a','b')) -cne 'compiled-isolated:2') { throw 'The isolated compiled block was not executed.' }
    if ($isolated.Ast.Extent.StartLineNumber -ne 3 -or $isolated.Ast.Extent.File -ne 'isolated.psm1') { throw 'Isolated source positions changed.' }
    foreach ($value in 'alpha','東京','') {
        $original=@(Invoke-Original -Value $value) -join "`n"
        $compiled=@(Invoke-Compiled -Value $value) -join "`n"
        if ($original -cne $compiled) { throw "Script block mismatch: original=$original compiled=$compiled" }
    }
    if ([Generic.Compiler.StatementErrors.NativeScriptBlockFixture]::CompiledInvocations -ne 6) { throw 'The compiled callbacks were not executed.' }
    'Native script block scope qualification passed.'
} finally { Remove-Module $module }
