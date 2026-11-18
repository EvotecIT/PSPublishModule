[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ImportPath,
    [Parameter(Mandatory)][ValidateSet('Original', 'Built', 'Installed')][string] $Surface,
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$sourcePath = 'Helpers.ps1'
$ownedNames = @(
    'Get-SHA512Hash', 'Write-Log', 'Write-InvocationLog', 'DeepCopy-Object',
    'New-TemporaryDirectory', 'Write-InteractiveHost', 'Resolve-UnverifiedPath',
    'Ensure-Directory', 'Get-HttpWebResponseContent', 'New-ErrorRecord'
)
$root = [IO.Path]::GetFullPath($ScenarioRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container) -or
    (Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'The observer requires an existing scenario directory without links.'
}
foreach ($name in 'helpers.log', 'ensured') {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Observer fixture must not be a link.' }
        Remove-Item -LiteralPath $path -ErrorAction Stop
    }
}

if ($Surface -eq 'Original') {
    # This pinned input is a helper library, so its unchanged file is loaded into
    # the same module scope used by the compiled binary-module artifact.
    $module = New-Module -Name GitHubHelpers -ScriptBlock { param($Path) . $Path } -ArgumentList $ImportPath
    Import-Module $module -Force -DisableNameChecking
} else {
    $module = Import-Module -Name $ImportPath -Force -PassThru -DisableNameChecking -ErrorAction Stop
}
& $module {
    param($Root)
    $script:ObserverRoot = $Root
    function script:Get-GitHubConfiguration {
        param([string] $Name)
        if ($Name -eq 'LogPath') { return Join-Path $script:ObserverRoot 'helpers.log' }
        return $false
    }
    function script:Get-Date { [datetime]'2026-01-02T03:04:05' }
} $root

$cases = @(
    @{ id = 'sha512'; name = 'Get-SHA512Hash'; run = {
        $hash = Get-SHA512Hash -PlainText 'PowerForge'
        if ($hash.Length -ne 128 -or $hash -notmatch '^[0-9A-F]+$') { throw 'Invalid SHA512 result.' }
        $hash
    } },
    @{ id = 'logging'; name = 'Write-Log'; run = {
        Write-Log -Message 'fixture message' -Path (Join-Path $root 'helpers.log') 6>$null
        $log = Get-Content -LiteralPath (Join-Path $root 'helpers.log') -Raw
        if ($log -notmatch '2026-01-02 03:04:05.*INFORMATIONAL : fixture message') { throw 'Missing log record.' }
        $log.TrimEnd()
    } },
    @{ id = 'invocation-redaction'; name = 'Write-InvocationLog'; run = {
        $text = & $module {
            function Invoke-ObserverCall {
                param([string] $Secret)
                Write-InvocationLog -Invocation $MyInvocation -RedactParameter Secret -Verbose 4>&1
            }
            Invoke-ObserverCall -Secret 'fixture-secret'
        } | Out-String
        if ($text -notmatch '<redacted>' -or $text -match 'fixture-secret') { throw 'Invocation redaction failed.' }
        # The original file has no manifest version; packaging assigns one.
        ($text.Trim() -replace '^\[[^\]]*\]', '[module-version]')
    } },
    @{ id = 'deep-copy'; name = 'DeepCopy-Object'; run = {
        $value = [pscustomobject]@{ Name = 'original'; Nested = [pscustomobject]@{ Number = 42 } }
        $copy = DeepCopy-Object -InputObject $value
        $copy.Nested.Number = 7
        if ($value.Nested.Number -ne 42 -or $copy.Nested.Number -ne 7) { throw 'Deep copy shares nested storage.' }
        "$($copy.Name)|$($value.Nested.Number)|$($copy.Nested.Number)"
    } },
    @{ id = 'temporary-directory'; name = 'New-TemporaryDirectory'; run = {
        $directory = New-TemporaryDirectory
        try {
            $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
            if ($directory.Parent.FullName.TrimEnd('\', '/') -ne $expectedParent -or
                -not (Test-Path -LiteralPath $directory.FullName -PathType Container)) { throw 'Invalid temporary directory.' }
            $guid = [guid]::Parse($directory.Name)
            "DirectoryInfo|$($guid -ne [guid]::Empty)|exists"
        } finally {
            if ($directory.Parent.FullName.TrimEnd('\', '/') -eq $expectedParent) {
                Remove-Item -LiteralPath $directory.FullName -ErrorAction Stop
            }
        }
    } },
    @{ id = 'noninteractive-host'; name = 'Write-InteractiveHost'; run = {
        $records = @(Write-InteractiveHost -Object 'hidden' 6>&1)
        if ($records.Count) { throw 'Noninteractive host unexpectedly wrote a record.' }
        'silent'
    } },
    @{ id = 'unverified-path'; name = 'Resolve-UnverifiedPath'; run = {
        $path = Join-Path $root 'missing.txt'
        $resolved = Resolve-UnverifiedPath -Path $path
        if ($resolved -ne $path) { throw 'Missing path was not preserved.' }
        [IO.Path]::GetFileName($resolved)
    } },
    @{ id = 'ensure-directory'; name = 'Ensure-Directory'; run = {
        $path = Join-Path $root 'ensured'
        Ensure-Directory -Path $path
        Ensure-Directory -Path $path
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw 'Directory was not created.' }
        'created-and-reused'
    } },
    @{ id = 'http-response'; name = 'Get-HttpWebResponseContent'; run = {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $client = $null
        $response = $null
        try {
            $listener.Start()
            $request = [Net.WebRequest]::Create("http://127.0.0.1:$($listener.LocalEndpoint.Port)/")
            $request.Proxy = $null
            $request.Timeout = 10000
            $pending = $request.GetResponseAsync()
            $accept = $listener.AcceptTcpClientAsync()
            if (-not $accept.Wait(10000)) { throw 'Loopback request did not arrive.' }
            $client = $accept.GetAwaiter().GetResult()
            $stream = $client.GetStream()
            $stream.ReadTimeout = 10000
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
            while ($reader.ReadLine()) { }
            $bytes = [Text.Encoding]::UTF8.GetBytes("HTTP/1.1 200 OK`r`nContent-Length: 12`r`nConnection: close`r`n`r`nfixture-body")
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush()
            if (-not $pending.Wait(10000)) { throw 'Loopback response did not complete.' }
            $response = $pending.GetAwaiter().GetResult()
            $content = Get-HttpWebResponseContent -WebResponse $response
            if ($content -cne 'fixture-body') { throw 'HTTP content differs.' }
            $content
        } finally {
            if ($response) { $response.Dispose() }
            if ($client) { $client.Dispose() }
            $listener.Stop()
        }
    } },
    @{ id = 'error-record'; name = 'New-ErrorRecord'; run = {
        $record = New-ErrorRecord -ErrorMessage 'fixture error' -ErrorId 'FixtureFailure' -ErrorCategory InvalidData -TargetObject 'target'
        if ($record.FullyQualifiedErrorId -cne 'FixtureFailure' -or $record.TargetObject -cne 'target') { throw 'Error identity differs.' }
        "$($record.Exception.Message)|$($record.FullyQualifiedErrorId)|$($record.CategoryInfo.Category)|$($record.TargetObject)"
    } }
)
$observations = @(foreach ($case in $cases) {
    $records = @(& $case.run)
    [ordered]@{
        scenarioId = $case.id
        succeeded = $true
        executedExports = @($case.name)
        executedOwnedFunctions = @([ordered]@{ relativePath = $sourcePath; name = $case.name })
        observation = $records
    }
})
[ordered]@{
    schemaVersion = 1
    surface = $Surface
    importPath = [IO.Path]::GetFullPath($ImportPath)
    actualExports = @($module.ExportedFunctions.Keys | Sort-Object)
    actualAliases = @()
    scenarios = $observations
    limits = 'All ten helper functions execute. Configuration and time are fixed in the importing module; HTTP uses an owned loopback server. No GitHub service, exhaustive branch coverage or runtime-free execution is claimed.'
} | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $ResultPath -Encoding utf8
