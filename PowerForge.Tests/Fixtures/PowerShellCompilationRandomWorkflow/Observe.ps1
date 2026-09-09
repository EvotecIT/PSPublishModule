# The caller imports the original or generated module into $module.
# Replace only its random provider with a deterministic, traced boundary.
foreach ($name in 'Get-RandomCharacters', 'Get-RandomPassword') {
    if (!$module.ExportedCommands.ContainsKey($name)) { throw ('The selected module does not export ' + $name) }
}
& $module {
    $script:RandomTrace = [Collections.Generic.List[string]]::new()
    $script:RandomCalls = 0
    $script:RandomFault = 'none'
    function script:Get-Random {
        [CmdletBinding()]
        param(
            [int] $Maximum,
            [ValidateRange(1, 2147483647)][int] $Count = 1,
            [Parameter(ValueFromPipeline = $true)][object] $InputObject
        )
        begin {
            $items = [Collections.Generic.List[object]]::new()
            $maximumMode = $PSBoundParameters.ContainsKey('Maximum')
            $script:RandomTrace.Add('begin:' + $maximumMode)
        }
        process {
            $script:RandomCalls++
            $script:RandomTrace.Add('process:' + $script:RandomCalls + ':' + $Maximum + ':' + $InputObject)
            if ($script:RandomCalls -eq 2) {
                switch ($script:RandomFault) {
                    'error' { Write-Error 'injected random provider failure' -ErrorId RandomProviderFailure }
                    'throw' { throw 'injected random provider termination' }
                }
            }
            if ($maximumMode) { ($script:RandomCalls - 1) % $Maximum }
            else { $items.Add($InputObject) }
        }
        end {
            $script:RandomTrace.Add('end:' + $items.Count)
            if (!$maximumMode) {
                for ($i = $items.Count - 1; $i -ge 0 -and $i -ge $items.Count - $Count; $i--) { $items[$i] }
            }
        }
    }
}

function Describe-RandomError($fault) {
    $record = if ($fault -is [Management.Automation.ErrorRecord]) { $fault } else { $fault.ErrorRecord }
    $chain = @()
    $exception = $record.Exception
    while ($null -ne $exception) {
        $chain += $exception.GetType().FullName + ':' + $exception.Message
        $exception = $exception.InnerException
    }
    [pscustomobject]@{
        id = $record.FullyQualifiedErrorId
        chain = $chain
        line = $record.InvocationInfo.ScriptLineNumber
        column = $record.InvocationInfo.OffsetInLine
    }
}

$cases = @(
    @{ name = 'characters-zero'; command = 'Get-RandomCharacters'; arguments = @{ length = 0; characters = 'abc' } }
    @{ name = 'characters-one'; command = 'Get-RandomCharacters'; arguments = @{ length = 1; characters = 'abc' } }
    @{ name = 'characters-many'; command = 'Get-RandomCharacters'; arguments = @{ length = 4; characters = 'abc' } }
    @{ name = 'characters-negative'; command = 'Get-RandomCharacters'; arguments = @{ length = -1; characters = 'abc' } }
    @{ name = 'characters-empty'; command = 'Get-RandomCharacters'; arguments = @{ length = 4; characters = '' } }
    @{ name = 'characters-null'; command = 'Get-RandomCharacters'; arguments = @{ length = 4; characters = $null } }
    @{ name = 'characters-unicode'; command = 'Get-RandomCharacters'; arguments = @{ length = 4; characters = 'aą🙂' } }
    @{ name = 'characters-malformed'; command = 'Get-RandomCharacters'; arguments = @{ length = 'bad'; characters = 'abc' } }
    @{ name = 'characters-array'; command = 'Get-RandomCharacters'; arguments = @{ length = 4; characters = @('a', 'b') } }
    @{ name = 'password-default'; command = 'Get-RandomPassword'; arguments = @{} }
    @{ name = 'password-zero'; command = 'Get-RandomPassword'; arguments = @{ LettersLowerCase = 0; LettersHigherCase = 0; Numbers = 0; SpecialChars = 0; SpecialCharsLimited = 0 } }
    @{ name = 'password-one'; command = 'Get-RandomPassword'; arguments = @{ LettersLowerCase = 1; LettersHigherCase = 0; Numbers = 0; SpecialChars = 0; SpecialCharsLimited = 0 } }
    @{ name = 'password-many'; command = 'Get-RandomPassword'; arguments = @{ LettersLowerCase = 2; LettersHigherCase = 2; Numbers = 2; SpecialChars = 2; SpecialCharsLimited = 2 } }
    @{ name = 'password-negative'; command = 'Get-RandomPassword'; arguments = @{ LettersLowerCase = -1 } }
    @{ name = 'password-malformed'; command = 'Get-RandomPassword'; arguments = @{ Numbers = 'bad' } }
    @{ name = 'password-null'; command = 'Get-RandomPassword'; arguments = @{ Numbers = $null } }
)
foreach ($case in $cases) {
    foreach ($fault in 'none', 'error', 'throw') {
        foreach ($action in 'Continue', 'SilentlyContinue', 'Stop') {
            foreach ($stopEarly in $false, $true) {
                & $module { param($mode) $script:RandomTrace.Clear(); $script:RandomCalls = 0; $script:RandomFault = $mode } $fault
                $Error.Clear(); $records = @(); $faults = @(); $caught = $null
                $arguments = $case.arguments
                $command = $module.ExportedCommands[$case.command]
                try {
                    if ($stopEarly) {
                        $records = @(1..2 | ForEach-Object { & $command @arguments -ErrorAction $action -ErrorVariable +faults } 2>$null | Select-Object -First 1)
                    } else {
                        $records = @(& $command @arguments -ErrorAction $action -ErrorVariable +faults 2>$null)
                    }
                } catch { $caught = Describe-RandomError $_ }
                $trace = @(& $module { $script:RandomTrace.ToArray() })
                [pscustomobject]@{
                    case = $case.name; fault = $fault; action = $action; stop = $stopEarly
                    records = $records; types = @($records | ForEach-Object { $_.GetType().FullName })
                    trace = $trace; caught = $caught
                    faults = @($faults | ForEach-Object { Describe-RandomError $_ })
                    errors = @($Error | ForEach-Object { Describe-RandomError $_ })
                } | ConvertTo-Json -Compress -Depth 12
            }
        }
    }
}
& $module { $script:RandomCalls = 0; $script:RandomFault = 'none' }
'later:' + (& $module.ExportedCommands['Get-RandomCharacters'] -length 3 -characters 'abc')
