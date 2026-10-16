[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Start','Stop')][string] $Operation,
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath
)
$ErrorActionPreference = 'Stop'
if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
    throw 'This whole-script packet qualifies the explicit win-x64 native-entry contract.'
}
$ScenarioRoot = [IO.Path]::GetFullPath($ScenarioRoot)
if (-not (Test-Path -LiteralPath $ScenarioRoot -PathType Container)) { throw 'The canonical harness must create the owned scenario root.' }
$current = Get-Item -LiteralPath $ScenarioRoot
while ($null -ne $current) {
    if ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The fixture root cannot traverse links or junctions.' }
    $current = $current.Parent
}
$fixtureRoot = Join-Path $ScenarioRoot 'fixture'
$inputRoot = Join-Path $ScenarioRoot 'inputs'
$statePath = Join-Path $fixtureRoot 'state.json'
$partialPath = Join-Path $fixtureRoot 'start-attempt.json'
$serverScript = Join-Path $PSScriptRoot 'Serve-SitemapFixture.ps1'

function Test-OwnedServer($Identity) {
    $process = Get-Process -Id $Identity.pid -ErrorAction SilentlyContinue
    return $null -ne $process -and $process.StartTime.ToUniversalTime() -eq ([datetime]$Identity.startUtc).ToUniversalTime()
}

if ($Operation -eq 'Start') {
    if (Test-Path -LiteralPath $fixtureRoot) { throw 'Fixture evidence already exists; use a fresh canonical run root.' }
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    [ordered]@{schemaVersion=1;stage='Prepared';pid=$null;startUtc=$null} | ConvertTo-Json | Set-Content -LiteralPath $partialPath -Encoding utf8NoBOM
    New-Item -ItemType Directory -Path $inputRoot | Out-Null
    $urlset = '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">{URLS}</urlset>'
    $oldPaths = @('/stay/','/article/','/category/microsoft-widgets-en-2/','/docs/','/legacy.html','/amp/','/unmapped/','/gone/')
    $oldA = $urlset.Replace('{URLS}', (($oldPaths | Select-Object -First 4 | ForEach-Object { '<url><loc>{ORIGIN}' + $_ + '</loc></url>' }) -join ''))
    $oldB = $urlset.Replace('{URLS}', (($oldPaths | Select-Object -Skip 4 | ForEach-Object { '<url><loc>{ORIGIN}' + $_ + '</loc></url>' }) -join ''))
    $routes = @(
        [ordered]@{path='/index.xml';status=200;contentType='application/xml';body='<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><sitemap><loc>{ORIGIN}/old-a.xml</loc></sitemap><sitemap><loc>{ORIGIN}/old-b.xml</loc></sitemap></sitemapindex>'},
        [ordered]@{path='/old-a.xml';status=200;contentType='application/xml';body=$oldA},
        [ordered]@{path='/old-b.xml';status=200;contentType='application/xml';body=$oldB},
        [ordered]@{path='/empty-old.xml';status=200;contentType='application/xml';body=$urlset.Replace('{URLS}','<url><loc>{ORIGIN}/stay/</loc></url>')},
        [ordered]@{path='/malformed.xml';status=200;contentType='application/xml';body='<urlset><url>'}
    )
    foreach ($path in @($oldPaths + '/hub/scripts/sample/')) {
        $routes += [ordered]@{path=$path;status=if($path -eq '/gone/'){404}else{200};contentType='text/html; charset=utf-8';body=if($path -eq '/article/'){'<html><head><link rel="amphtml" href="/mobile/article/"></head><body>article</body></html>'}else{'<html><body>fixture</body></html>'}}
    }
    $routesPath = Join-Path $inputRoot 'routes.json'
    $routes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $routesPath -Encoding utf8NoBOM
    $readyPath = Join-Path $fixtureRoot 'ready.json'
    $requestsPath = Join-Path $fixtureRoot 'requests.jsonl'
    $serverArguments = @('-NoLogo','-NoProfile','-NonInteractive','-File',$serverScript,'-RoutesPath',$routesPath,'-ReadyPath',$readyPath,'-RequestLogPath',$requestsPath,'-DiagnosticRoot',$fixtureRoot,'-LeaseSeconds','3600')
    # Windows file names cannot contain quotes; all path operands here name files,
    # so none ends in a backslash before its closing process-argument quote.
    if (@($serverArguments | Where-Object { $_.Contains('"') -or $_.EndsWith('\') }).Count) { throw 'Invalid owned server argument.' }
    $argumentLine = ($serverArguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    # Shell execution prevents the retained child from inheriting the Start
    # caller's captured pipe handles. The worker writes its own diagnostics.
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path, $argumentLine)
    $start.UseShellExecute = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.WorkingDirectory = $ScenarioRoot
    $server = [Diagnostics.Process]::Start($start)
    $completed = $false
    try {
        [ordered]@{schemaVersion=1;stage='Starting';pid=$server.Id;startUtc=$server.StartTime.ToUniversalTime().ToString('o')} |
            ConvertTo-Json | Set-Content -LiteralPath $partialPath -Encoding utf8NoBOM
        $deadline = [datetime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $readyPath)) {
            if ($server.HasExited -or [datetime]::UtcNow -gt $deadline) { throw 'The owned loopback fixture failed to become ready.' }
            Start-Sleep -Milliseconds 50
        }
        $ready = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
        $owned = [ordered]@{pid=$server.Id;startUtc=$server.StartTime.ToUniversalTime().ToString('o');address='127.0.0.1';port=[int]$ready.Port;origin=[string]$ready.Origin;serverSha256=(Get-FileHash -LiteralPath $serverScript).Hash;leaseSeconds=3600}
        if ($ready.Pid -ne $owned.pid -or ([datetime]$ready.StartUtc).ToUniversalTime() -ne ([datetime]$owned.startUtc).ToUniversalTime() -or $ready.Address -ne $owned.address -or $ready.Origin -cne ('http://127.0.0.1:' + $owned.port)) { throw 'The ready receipt differs from its owned loopback process.' }
        $site = Join-Path $inputRoot 'site'
        New-Item -ItemType Directory -Path (Join-Path $site 'projects') | Out-Null
        $newUrls = @('/stay/','/blog/','/blog/article/','/categories/widgets/','/legacy/','/sample/')
        $newSitemap = Join-Path $site 'sitemap.xml'
        [IO.File]::WriteAllText($newSitemap, $urlset.Replace('{URLS}', (($newUrls | ForEach-Object { '<url><loc>' + $owned.origin + $_ + '</loc></url>' }) -join '')))
        [IO.File]::WriteAllText((Join-Path $site 'projects/index.html'), '<html>owned project route</html>')
        [IO.File]::WriteAllText((Join-Path $inputRoot 'same.xml'), $urlset.Replace('{URLS}', '<url><loc>' + $owned.origin + '/stay/</loc></url>'))
        [IO.File]::WriteAllText((Join-Path $inputRoot 'malformed.xml'), '<urlset><url>')
        $outputRoot = Join-Path $ScenarioRoot 'ProjectTest'
        New-Item -ItemType Directory -Path $outputRoot | Out-Null
        $arguments = @(('--LegacySitemapUrls=' + $owned.origin + '/index.xml'), ('--NewSitemapPath=' + $newSitemap),
            ('--AdditionalLegacyUrls=' + $owned.origin + '/hub/scripts/sample/'), '--DiscoverAmpHtml', '--IncludeAmpListingRoots',
            '--TimeoutSec=3', '--MaxSitemapDepth=4', ('--OutputJsonPath=' + (Join-Path $outputRoot 'report.json')),
            ('--OutputCsvPath=' + (Join-Path $outputRoot 'missing.csv')), ('--OutputRedirectCsvPath=' + (Join-Path $outputRoot 'redirects.csv')),
            ('--OutputReviewCsvPath=' + (Join-Path $outputRoot 'review.csv')))
        $inputHashes = @(Get-ChildItem -LiteralPath $inputRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
            [ordered]@{path=[IO.Path]::GetRelativePath($ScenarioRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
        })
        $state = [ordered]@{schemaVersion=1;fixtureIdentity=$owned;inputHashes=$inputHashes;testArguments=$arguments;testWorkingDirectory='scenarios'}
        $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
        [ordered]@{schemaVersion=1;operation='Start';testArguments=$arguments;testWorkingDirectory='scenarios';fixtureIdentity=$owned} |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM
        $completed = $true
    } finally {
        if (-not $completed -and -not $server.HasExited) {
            $server.Kill($true)
            $server.WaitForExit()
        }
        $server.Dispose()
    }
} else {
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        if (-not (Test-Path -LiteralPath $partialPath -PathType Leaf)) { throw 'The fixture has no owned Start or recovery receipt.' }
        $partial = Get-Content -LiteralPath $partialPath -Raw | ConvertFrom-Json
        if ($partial.schemaVersion -ne 1 -or $partial.stage -notin @('Prepared','Starting')) { throw 'Invalid partial Start recovery receipt.' }
        if ($null -ne $partial.pid -and (Test-OwnedServer $partial)) {
            $process = Get-Process -Id $partial.pid -ErrorAction Stop
            if (Test-OwnedServer $partial) { $process.Kill($true); $process.WaitForExit() }
        }
        $absent = $null -eq $partial.pid -or -not (Test-OwnedServer $partial)
        [ordered]@{schemaVersion=1;operation='Stop';stopped=$absent;fixtureIdentity=$partial;recoveredPartialStart=$true;ownedIdentityAbsent=$absent;noUnrelatedProcessStopped=$true} |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM
        if (-not $absent) { throw 'Partial Start recovery could not release its exact owned process.' }
        exit 0
    }
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $owned = $state.fixtureIdentity
    if ($owned.address -ne '127.0.0.1' -or $owned.origin -cne ('http://127.0.0.1:' + $owned.port)) { throw 'The fixture identity does not name the owned loopback server.' }
    $inputsStable = $false
    $serverSourceStable = $false
    try {
        $inputsStable = @($state.inputHashes | Where-Object { (Get-FileHash -LiteralPath (Join-Path $ScenarioRoot $_.path)).Hash -ne $_.sha256 }).Count -eq 0
        $serverSourceStable = $owned.serverSha256 -eq (Get-FileHash -LiteralPath $serverScript).Hash
    } catch { }
    if (Test-OwnedServer $owned) {
        $client = [Net.Http.HttpClient]::new()
        try {
            $client.Timeout = [TimeSpan]::FromSeconds(3)
            try { $null = $client.GetStringAsync($owned.origin + '/__shutdown').GetAwaiter().GetResult() } catch { }
        } finally { $client.Dispose() }
        $process = Get-Process -Id $owned.pid -ErrorAction SilentlyContinue
        if ($null -ne $process -and (Test-OwnedServer $owned) -and -not $process.WaitForExit(5000)) {
            if (Test-OwnedServer $owned) { $process.Kill($true); $process.WaitForExit() }
        }
    }
    $identityAbsent = -not (Test-OwnedServer $owned)
    $listeners = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object Port -eq $owned.port)
    $stopped = $identityAbsent -and $listeners.Count -eq 0 -and $inputsStable -and $serverSourceStable
    [ordered]@{schemaVersion=1;operation='Stop';stopped=$stopped;fixtureIdentity=$owned;ownedIdentityAbsent=$identityAbsent;listenersOnRecordedPort=$listeners.Count;inputHashesStable=$inputsStable;serverSourceHashStable=$serverSourceStable;noUnrelatedProcessStopped=$true} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM
    if (-not $stopped) { throw 'Fixture shutdown, listener absence or immutable input verification failed; see the Stop receipt.' }
}
