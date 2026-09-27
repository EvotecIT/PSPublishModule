param([Parameter(Mandatory)][string]$Assembly)
$ErrorActionPreference = 'Stop'
Add-Type -Path $Assembly
$module = New-Module -Name AuthoredMetadataOwner -ScriptBlock {
    class AuthoredTransform : System.Management.Automation.ArgumentTransformationAttribute {
        [object] Transform([System.Management.Automation.EngineIntrinsics]$engine, [object]$value) {
            if ($null -eq $value) { return $null }
            return ([string]$value).ToUpperInvariant()
        }
    }
    class AuthoredValidation : System.Management.Automation.ValidateArgumentsAttribute {
        [void] Validate([object]$value, [System.Management.Automation.EngineIntrinsics]$engine) {
            if ($value -eq 'BAD') { throw 'Rejected authored value.' }
        }
    }
    enum AuthoredMode { Alpha; Beta }
    $script:Marker = 'owned'
    function Get-DefaultValue { 'seed' }
    function Invoke-Original {
        [CmdletBinding()]
        param([Parameter(ValueFromPipeline)][AuthoredTransform()][AuthoredValidation()][string]$Value = (Get-DefaultValue),
            [AuthoredMode]$Mode = [AuthoredMode]::Alpha)
        begin { $Count = 0 }
        process { $Count++; "$Value|$Mode|$script:Marker|$Count" }
        end { "end:$Count" }
    }
    function Invoke-Compiled {
        [CmdletBinding()]
        param([Parameter(ValueFromPipeline)][AuthoredTransform()][AuthoredValidation()][string]$Value = (Get-DefaultValue),
            [AuthoredMode]$Mode = [AuthoredMode]::Alpha)
        begin { $Count = 0 }
        process { $Count++; "$Value|$Mode|$script:Marker|$Count" }
        end { "end:$Count" }
    }
    $script:BeforeScript = (Get-Command Invoke-Compiled).ScriptBlock
    $script:BeforeText = $script:BeforeScript.ToString()
    [Generic.Compiler.StatementErrors.NativeAuthoredMetadataFixture]::Install($ExecutionContext.SessionState.Module, 'Invoke-Compiled')
    Export-ModuleMember -Function Invoke-Original, Invoke-Compiled
}
Import-Module $module
try {
    $before = $module.Invoke({ $script:BeforeScript })[0]
    $beforeText = [string]$module.Invoke({ $script:BeforeText })
    $after = $module.Invoke({ (Get-Command Invoke-Compiled).ScriptBlock })[0]
    if ($beforeText -cne $after.ToString() -or $beforeText -cne $before.ToString()) {
        throw 'Declaration source identity changed.'
    }
    $original = Get-Command Invoke-Original
    $compiled = Get-Command Invoke-Compiled
    foreach ($name in 'Value', 'Mode') {
        if ($original.Parameters[$name].ParameterType -ne $compiled.Parameters[$name].ParameterType) {
            throw "Parameter type identity changed: $name"
        }
        $left = @($original.Parameters[$name].Attributes | ForEach-Object { $_.GetType() })
        $right = @($compiled.Parameters[$name].Attributes | ForEach-Object { $_.GetType() })
        if ($left.Count -ne $right.Count) { throw "Attribute count changed: $name" }
        for ($index = 0; $index -lt $left.Count; $index++) {
            if ($left[$index] -ne $right[$index]) { throw "Attribute type identity changed: $name" }
        }
    }
    foreach ($iteration in 1..3) {
        foreach ($arguments in @{}, @{ Value = 'hello'; Mode = 'Beta' }, @{ Value = $null }) {
            $left = @(Invoke-Original @arguments)
            $right = @(Invoke-Compiled @arguments)
            if (($left -join "`n") -cne ($right -join "`n")) { throw 'Bound-value output mismatch.' }
        }
        $left = @('one', 'two' | Invoke-Original -Mode Beta)
        $right = @('one', 'two' | Invoke-Compiled -Mode Beta)
        if (($left -join "`n") -cne ($right -join "`n")) { throw 'Pipeline output mismatch.' }
        $errors = foreach ($name in 'Invoke-Original', 'Invoke-Compiled') {
            try { & $name -Value bad; throw 'Invalid authored value was accepted.' }
            catch { $_.FullyQualifiedErrorId -replace ',Invoke-(Original|Compiled)$', ',Invoke-Function' }
        }
        if ($errors[0] -cne $errors[1] -or $errors[0] -notlike 'ParameterArgumentValidationError*') {
            throw "Validation error mismatch: $errors"
        }
    }
    if ([Generic.Compiler.StatementErrors.NativeAuthoredMetadataFixture]::CallbackCount -ne 15) {
        throw "The compiled callback did not execute the expected records: $([Generic.Compiler.StatementErrors.NativeAuthoredMetadataFixture]::CallbackCount)"
    }
    $savedOutput = @(& $before -Value hello)
    if (($savedOutput -join "`n") -cne "HELLO|Alpha|owned|1`nend:1") { throw 'The saved original body changed.' }
    if ([Generic.Compiler.StatementErrors.NativeAuthoredMetadataFixture]::CallbackCount -ne 15) {
        throw 'Replacing one function changed a saved original body.'
    }
    'Native authored metadata qualification passed: 15 compiled records; transformation, validation, enum identity, defaults, pipeline, repeat and original isolation.'
}
finally { Remove-Module $module }
