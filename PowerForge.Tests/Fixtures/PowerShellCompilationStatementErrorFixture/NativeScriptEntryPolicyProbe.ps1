param([string] $Assembly)
$ErrorActionPreference = 'Stop'
Add-Type -Path $Assembly
$taskRoot = Join-Path ([IO.Path]::GetTempPath()) ('pfc-native-entry-policy-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $taskRoot -ErrorAction Stop
try {
    $source = 'param([Generic.Compiler.StatementErrors.NativeScriptEntryBinding()] $Value = $($global:BindingRuns++; ''ok'')); ''ran'''
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($source)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    # PowerShell's ShouldRunInternal returns without calling authorization managers on Unix.
    # Preserve that native behavior while proving the compiled path matches authored dispatch.
    $nativeAuthorizationApplies = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    $observations = @{}
    foreach ($case in @('original-allow', 'compiled-allow', 'original-deny', 'compiled-deny', 'source-mismatch', 'restricted')) {
        # Distinct paths isolate native metadata caches while source contents and hash stay identical.
        $sourcePath = Join-Path $taskRoot ($case + '.ps1')
        [IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($true))
        [Generic.Compiler.StatementErrors.NativeScriptEntryAuthorization]::Checks = 0
        [Generic.Compiler.StatementErrors.NativeScriptEntryAuthorization]::Reject = $case.EndsWith('-deny')
        [Generic.Compiler.StatementErrors.NativeScriptEntryPolicyFixture]::CallbackInvocations = 0
        [Generic.Compiler.StatementErrors.NativeScriptEntryBindingAttribute]::Constructions = 0
        $initial = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault2()
        $initial.AuthorizationManager = [Generic.Compiler.StatementErrors.NativeScriptEntryAuthorization]::new()
        $runspace = [runspacefactory]::CreateRunspace($initial)
        $runspace.Open()
        $ps = [powershell]::Create()
        $ps.Runspace = $runspace
        try {
            $null = $ps.AddScript('$global:BindingRuns = 0')
            $null = $ps.Invoke()
            $ps.Commands.Clear()
            if ($case.StartsWith('original-')) { $null = $ps.AddCommand($sourcePath) }
            else {
                $method = if ($case -eq 'restricted') { 'CreateRestricted' } else { 'Create' }
                $wrapper = 'param($Path,$Hash) try { $info = [Generic.Compiler.StatementErrors.NativeScriptEntryPolicyFixture]::' + $method + '($ExecutionContext.SessionState,$Host,$Path,$Hash); & $info } catch { throw }'
                $null = $ps.AddScript($wrapper, $false)
                $null = $ps.AddParameter('Path', $sourcePath)
                $selectedHash = if ($case -eq 'source-mismatch') { '0' * 64 } else { $hash }
                $null = $ps.AddParameter('Hash', $selectedHash)
            }
            $records = @()
            try { $records = @($ps.Invoke()) } catch { }
            $allowed = $case.EndsWith('-allow') -or ($case.EndsWith('-deny') -and -not $nativeAuthorizationApplies)
            $checks = [Generic.Compiler.StatementErrors.NativeScriptEntryAuthorization]::Checks
            $callbacks = [Generic.Compiler.StatementErrors.NativeScriptEntryPolicyFixture]::CallbackInvocations
            $bindings = $runspace.SessionStateProxy.GetVariable('BindingRuns')
            $attributes = [Generic.Compiler.StatementErrors.NativeScriptEntryBindingAttribute]::Constructions
            Write-Output ('Native entry policy observation: {0}; errors={1}; records={2}; checks={3}; binding={4}; attributes={5}; callbacks={6}' -f
                $case, $ps.HadErrors, $records.Count, $checks, $bindings, $attributes, $callbacks)
            if ($allowed) {
                if ($ps.HadErrors -or $records.Count -ne 1 -or $records[0] -cne 'ran' -or $bindings -ne 1 -or $attributes -ne 1) { throw "Allowed invocation failed: $case" }
                $expectedCallbacks = if ($case.StartsWith('compiled-')) { 1 } else { 0 }
                if ($callbacks -ne $expectedCallbacks) { throw "Unexpected callback count: $case" }
            } elseif (-not $ps.HadErrors -or $records.Count -ne 0 -or $callbacks -ne 0 -or $bindings -ne 0 -or $attributes -ne 0) { throw "Rejected invocation reached binding/body: $case" }
            $expectedChecks = if ($nativeAuthorizationApplies -and $case -notin @('restricted', 'source-mismatch')) { 1 } else { 0 }
            if ($checks -ne $expectedChecks) { throw "Native authorization count mismatch: $case ($checks)" }
            $observations[$case] = [pscustomobject]@{
                HadErrors = $ps.HadErrors; RecordCount = $records.Count
                Checks = $checks; BindingRuns = $bindings; Attributes = $attributes
            }
            if ($case.StartsWith('compiled-')) {
                $original = $observations[$case.Replace('compiled-', 'original-')]
                foreach ($property in @('HadErrors', 'RecordCount', 'Checks', 'BindingRuns', 'Attributes')) {
                    if ($observations[$case].$property -ne $original.$property) { throw "Native entry policy differential mismatch: $case ($property)" }
                }
            }
        } finally { $ps.Dispose(); $runspace.Dispose() }
    }
    'Native script entry policy comparison passed: platform={0}; authored/compiled native authorization behavior, source mismatch and restricted language.' -f [Environment]::OSVersion.Platform
} finally { Remove-Item -LiteralPath $taskRoot -Recurse -ErrorAction Stop }
