[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Start', 'Stop')][string] $Operation,
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath,
    [Parameter(Mandatory)][ValidateSet('powershell', 'scoop')][string] $Installer
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot '../../Corpus.Runner.Common.ps1')
if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
    throw 'The installer contracts require Windows and process-local registry isolation.'
}
$root = [IO.Path]::GetFullPath($ScenarioRoot)
if ($Installer -eq 'scoop' -and $root -match '[^\x00-\x7F]') { throw 'The pinned Scoop installer writes ASCII configuration; supply an ASCII -WorkspacePath.' }
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'The canonical harness must create the scenario root.' }
$ancestor = Get-Item -LiteralPath $root
while ($null -ne $ancestor) {
    if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installer fixtures cannot traverse links.' }
    $ancestor = $ancestor.Parent
}
$fixtureRoot = Join-Path $root 'installer-fixture'
$statePath = Join-Path $fixtureRoot 'state.json'
$partialPath = Join-Path $fixtureRoot 'start-attempt.json'
$serverScript = Join-Path $PSScriptRoot 'Serve-InstallerPayloads.ps1'
function Test-InstallerServerOwner($Identity) {
    $process = Get-Process -Id $Identity.pid -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $false }
    try { return $process.StartTime.ToUniversalTime() -eq ([datetime] $Identity.startUtc).ToUniversalTime() }
    catch {
        if ($null -eq (Get-Process -Id $Identity.pid -ErrorAction SilentlyContinue)) { return $false }
        throw
    } finally { $process.Dispose() }
}

if ($Operation -eq 'Start') {
    if (Test-Path -LiteralPath $fixtureRoot) { throw 'Installer fixture state already exists; use a fresh scenario root.' }
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    [ordered]@{schemaVersion=1;pid=$null;startUtc=$null} | ConvertTo-Json |
        Set-Content -LiteralPath $partialPath -Encoding utf8
    $cache = $env:POWERFORGE_INSTALLER_PAYLOAD_CACHE
    if (-not $cache) { $cache = Join-Path ([IO.Path]::GetTempPath()) 'PowerForge/InstallerPayloads' }
    $pins = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-payloads.json') -Raw | ConvertFrom-Json
    $inputs = @(foreach ($pin in $pins.payloads) {
        $file = Get-VerifiedCorpusPayload -Uri $pin.uri -Sha256 $pin.sha256 -CacheRoot $cache -CacheExtension '.zip' -Label ('installer ' + $pin.id)
        if ((Get-Item -LiteralPath $file).Length -ne $pin.bytes) { throw 'Pinned installer payload size differs.' }
        [ordered]@{id=$pin.id;path=$file;sha256=$pin.sha256;bytes=$pin.bytes}
    })
    $acceptance = Join-Path $root 'acceptance'
    New-Item -ItemType Directory -Path $acceptance | Out-Null
    $observerName = if ($Installer -eq 'powershell') { 'Observe-PowerShellInstaller.ps1' } else { 'Observe-ScoopInstaller.ps1' }
    foreach ($name in @($observerName, 'Test-InstallerModule.ps1', 'Installer.Observer.Common.ps1', 'InstallerRegistryScope.cs', 'installer-payloads.json')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $acceptance $name)
    }
    $readyPath = Join-Path $fixtureRoot 'ready.json'
    $requestsPath = Join-Path $fixtureRoot 'requests.jsonl'
    $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $serverScript,
        '-PayloadRoot', ([IO.Path]::GetFullPath($cache)), '-ReadyPath', $readyPath, '-RequestLogPath', $requestsPath, '-DiagnosticRoot', $fixtureRoot)
    if (@($arguments | Where-Object { $_.Contains('"') -or $_.EndsWith('\') }).Count) { throw 'Invalid installer fixture process argument.' }
    $line = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path, $line)
    $start.UseShellExecute = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process = [Diagnostics.Process]::Start($start)
    try {
        $owner = [ordered]@{pid=$process.Id;startUtc=$process.StartTime.ToUniversalTime().ToString('o')}
        $ownerTemporary = $partialPath + '.owner.tmp'
        $owner | ConvertTo-Json | Set-Content -LiteralPath $ownerTemporary -Encoding utf8
        [IO.File]::Move($ownerTemporary, $partialPath, $true)
    } catch {
        try { if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() } }
        finally { $process.Dispose() }
        throw
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $readyPath)) {
        if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'The owned installer payload server did not become ready.' }
        Start-Sleep -Milliseconds 100
    }
    $ready = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
    if ($ready.pid -ne $owner.pid -or ([datetime] $ready.startUtc).ToUniversalTime() -ne ([datetime] $owner.startUtc).ToUniversalTime() -or
        $ready.address -cne '127.0.0.1' -or $ready.origin -cne ('http://127.0.0.1:' + $ready.port)) { throw 'Owned installer server identity differs.' }
    $state = [ordered]@{schemaVersion=1;installer=$Installer;fixtureIdentity=$ready;payloads=$inputs;serverSha256=(Get-FileHash -LiteralPath $serverScript).Hash}
    $stateTemporary = $statePath + '.tmp'
    $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $stateTemporary -Encoding utf8
    [IO.File]::Move($stateTemporary, $statePath)
    $testScript = Join-Path $acceptance 'Test-InstallerModule.ps1'
    $moduleTest = [ordered]@{
        script='scenarios/acceptance/Test-InstallerModule.ps1'
        sha256=(Get-FileHash -LiteralPath $testScript).Hash.ToLowerInvariant()
        parameters=[ordered]@{ ObserverName=$observerName; ScenarioRoot=$root; ResultPath=(Join-Path $fixtureRoot 'project-test.json') }
        workingDirectory='scenarios'
    }
    [ordered]@{schemaVersion=1;operation='Start';testArguments=@();moduleTest=$moduleTest;fixtureIdentity=$ready} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultPath -Encoding utf8
} else {
    $state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
    $owner = if ($state) { $state.fixtureIdentity } else { Get-Content -LiteralPath $partialPath -Raw | ConvertFrom-Json }
    if ($null -ne $owner.pid -and (Test-InstallerServerOwner $owner)) {
        if ($state) {
            $handler = [Net.Http.HttpClientHandler]::new()
            $handler.UseProxy = $false
            $client = [Net.Http.HttpClient]::new($handler)
            try {
                $client.Timeout = [TimeSpan]::FromSeconds(3)
                try { $null = $client.GetStringAsync($owner.origin + '/__shutdown').GetAwaiter().GetResult() } catch { }
            } finally { $client.Dispose() }
        }
        $process = Get-Process -Id $owner.pid -ErrorAction SilentlyContinue
        if ($process -and (Test-InstallerServerOwner $owner) -and -not $process.WaitForExit(5000)) {
            if (Test-InstallerServerOwner $owner) { $process.Kill($true); $process.WaitForExit() }
        }
    }
    $stable = $true
    if ($state) {
        $stable = $state.serverSha256 -ceq (Get-FileHash -LiteralPath $serverScript).Hash -and
            @($state.payloads | Where-Object { (Get-FileHash -LiteralPath $_.path).Hash -ine $_.sha256 }).Count -eq 0
    }
    $absent = $null -eq $owner.pid -or -not (Test-InstallerServerOwner $owner)
    $listeners = if ($state) { @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $owner.port).Count } else { 0 }
    $stopped = $absent -and $stable
    [ordered]@{schemaVersion=1;operation='Stop';stopped=$stopped;fixtureIdentity=$owner;ownedIdentityAbsent=$absent;listenersOnRecordedPort=$listeners;inputHashesStable=$stable;noUnrelatedProcessStopped=$true} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultPath -Encoding utf8
    if (-not $stopped) { throw 'Installer fixture shutdown or immutable payload verification failed.' }
}
