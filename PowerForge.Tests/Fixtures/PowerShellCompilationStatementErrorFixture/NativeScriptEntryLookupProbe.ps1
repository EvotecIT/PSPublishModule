param([string] $Assembly)
$ErrorActionPreference = 'Stop'
Add-Type -Path $Assembly
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$taskRoot = Join-Path $tempRoot ('pfc-native-entry-lookup-' + [guid]::NewGuid().ToString('N'))
if (-not [IO.Path]::GetFullPath($taskRoot).StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Probe output escaped its temporary root.' }
$null = New-Item -ItemType Directory -Path $taskRoot -ErrorAction Stop
try {
    $nativeAuthorizationApplies = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    foreach ($kind in @('arguments','body-throw','binding','mandatory','predecessor-other','predecessor-command','predecessor-null','predecessor-error','identity','replacement','authorization','exception-priority')) {
        $result = [Generic.Compiler.StatementErrors.NativeScriptEntryLookupFixture]::Run($taskRoot, $kind)
        Write-Output ('Native entry lookup: {0}; state={1}; callbacks={2}; authored={3}; authorization={4}; restored={5}; failure={6}' -f
            $kind,$result.State,$result.Callbacks,$result.AuthoredRuns,$result.AuthorizationChecks,$result.HookRestored,$result.FailureType)
        if (-not $result.HookRestored -or $result.Lookups -ne 1) { throw "Lookup lifetime mismatch: $kind" }
        if ($kind -eq 'arguments') {
            $expected = @('compiled','value=owned','flag=False','[head]','[-Unknown]',"[a'b;`$(throw 'argument executed')]",'[-Unknown]','[second]','[tail]')
            if ($result.Callbacks -ne 1 -or $result.AuthoredRuns -ne 0 -or $result.FailureType -or $result.Records.Count -ne $expected.Count) { throw 'Native ordered argument dispatch failed.' }
            for ($index = 0; $index -lt $expected.Count; $index++) { if ($result.Records[$index] -cne $expected[$index]) { throw "Ordered argument record mismatch: $index" } }
        } elseif ($kind -eq 'replacement') {
            if ($result.Callbacks -ne 1 -or $result.AuthoredRuns -ne 0 -or $result.FailureType -or $result.Records.Count -ne 1 -or $result.Records[0] -cne 'compiled') { throw 'A hook installed by the body was not preserved.' }
        } elseif ($kind -in @('predecessor-other','predecessor-command')) {
            $expected = if ($kind -eq 'predecessor-other') { 'other' } else { 'deliberate command' }
            $expectedAuthored = if ($kind -eq 'predecessor-other') { 1 } else { 0 }
            if ($result.Callbacks -ne 0 -or $result.AuthoredRuns -ne $expectedAuthored -or $result.FailureType -or $result.Records.Count -ne 1 -or $result.Records[0] -cne $expected) { throw "Predecessor command replacement was overridden: $kind" }
        } elseif ($kind -eq 'authorization' -and -not $nativeAuthorizationApplies) {
            if ($result.Callbacks -ne 1 -or $result.FailureType) { throw 'Unix native authorization behavior changed.' }
        } else {
            $expectedCallbacks = if ($kind -eq 'body-throw') { 1 } else { 0 }
            if ($result.Callbacks -ne $expectedCallbacks -or $result.AuthoredRuns -ne 0 -or $result.Records.Count -ne 0 -or -not $result.FailureType -or $result.State -ne 'Failed') { throw "Rejected invocation reached source or callback: $kind" }
            if ($kind -eq 'predecessor-error' -and $result.FailureMessage -cne 'predecessor lookup failed') { throw 'Predecessor failure was swallowed.' }
            if ($kind -eq 'identity' -and $result.FailureMessage -notlike '*differs from its prepared native entry*') { throw 'Changed source did not fail its identity check.' }
            if ($kind -eq 'exception-priority' -and $result.FailureMessage -cne 'invocation boundary failed') { throw 'Hook cleanup masked an existing invocation failure.' }
        }
        $expectedChecks = if ($nativeAuthorizationApplies -and $kind -notin @('predecessor-error','identity','predecessor-command','predecessor-null','exception-priority')) { 1 } else { 0 }
        if ($result.AuthorizationChecks -ne $expectedChecks) { throw "Authorization ordering mismatch: $kind" }
    }
    'Native script entry lookup comparison passed: ordered data, lazy callbacks, binding/body/authorization refusal, source identity, predecessor ownership and scoped restoration.'
} finally { Remove-Item -LiteralPath $taskRoot -Recurse -ErrorAction Stop }
