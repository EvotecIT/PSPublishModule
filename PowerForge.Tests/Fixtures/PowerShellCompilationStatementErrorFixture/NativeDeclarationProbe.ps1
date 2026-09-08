param([Parameter(Mandatory)][string]$Assembly)
$ErrorActionPreference='Stop'
Add-Type -Path $Assembly
$cases=@(
    @{Targets=@('[int]$x','$x'); Operations=@('Assign','Assign'); Values=@('3',"'bad'"); Slots=@('[int]$x')},
    @{Targets=@('[int]$x','[int]$x','[int]$x'); Operations=@('Assign','Add','Assign'); Values=@("'4'",'3',"'bad'"); Slots=@('[int]$x')},
    @{Targets=@('[int]$x','[int]$x','[string]$x','[int]$x'); Operations=@('Assign','Add','Assign','Assign'); Values=@("'4'",'3',"'hello'","'bad'")},
    @{Targets=@('[ValidateRange(1,9)][int]$x','[ValidateRange(1,3)][int]$x','[string]$x'); Operations=@('Assign','Assign','Assign'); Values=@('5','8',"'replaced'")},
    @{Targets=@('[int]$x','[int]$x','[int]$x','[int]$x','[int]$x','[int]$x'); Operations=@('Assign','Add','Subtract','Multiply','Divide','Remainder'); Values=@('9','3','2','4','3','7')},
    @{Targets=@('[string]$x','[int]$x'); Operations=@('Assign','Add'); Values=@("'bad'",'$( $Trace += "rhs"; 3 )')},
    @{Targets=@('[int]$x','[int]$x'); Operations=@('Assign','Add'); Values=@('4','$( $Trace += "rhs"; $x=100; 3 )')},
    @{Targets=@('[ValidateScript({ $script:ValidationTrace += "v"; $_ -lt 5 })][int]$x','[ValidateScript({ $script:ValidationTrace += "v"; $_ -lt 5 })][int]$x'); Operations=@('Assign','Assign'); Values=@('3','7')}
)
$tokens=@{Assign='=';Add='+=';Subtract='-=';Multiply='*=';Divide='/=';Remainder='%='}
foreach($case in $cases) {
    $observations=@()
    foreach($compiled in $false,$true) {
        $module=New-Module -ScriptBlock { $ValidationTrace='' }
        if($compiled) {
            $slots=[string[]]@(); if($case.Slots) { $slots=[string[]]$case.Slots }
            $body=[Generic.Compiler.StatementErrors.NativeDeclarationFixture]::Create($module,[string[]]$case.Targets,[string[]]$case.Operations,[string[]]$case.Values,$slots)
        } else {
            $lines=@('[CmdletBinding()] param()','$x=2; $Trace=""')
            if($case.Slots) { $lines=@('[CmdletBinding()] param()',($case.Slots[0]+'=2; $Trace=""')) }
            for($i=0;$i -lt $case.Targets.Count;$i++) {
                $lines+='try { $result=('+ $case.Targets[$i] + ' ' + $tokens[$case.Operations[$i]] + ' (' + $case.Values[$i] + ')); "result=" + $(if($null -eq $result){"null"}else{$result.GetType().FullName + ":" + $result}) } catch { "error=" + $_.FullyQualifiedErrorId + ":" + $_.Exception.Message }'
                $lines+=[Generic.Compiler.StatementErrors.NativeDeclarationFixture]::Snapshot
            }
            $body=$module.NewBoundScriptBlock([scriptblock]::Create(($lines -join "`n")))
        }
        & $module { param($Body) Set-Item -LiteralPath Function:script:Test-Declaration -Value $Body } $body
        $records=@(& $module { Test-Declaration; 'validation='+$ValidationTrace })
        $observations+=ConvertTo-Json -InputObject $records -Depth 12 -Compress
    }
    if($observations[0] -cne $observations[1]) { throw "Declaration mismatch: $($case.Targets -join ',')`nOriginal: $($observations[0])`nGenerated: $($observations[1])" }
    $observations[1]
}
'Native declaration qualification passed.'
