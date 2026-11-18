[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PayloadRoot,
    [Parameter(Mandatory)][string] $ReadyPath,
    [Parameter(Mandatory)][string] $RequestLogPath,
    [Parameter(Mandatory)][string] $DiagnosticRoot,
    [ValidateRange(30, 3600)][int] $LeaseSeconds = 3600
)

$ErrorActionPreference = 'Stop'
trap {
    [IO.File]::WriteAllText((Join-Path $DiagnosticRoot 'server.stderr.txt'), ($_ | Out-String))
    exit 1
}
[IO.File]::WriteAllText((Join-Path $DiagnosticRoot 'server.stderr.txt'), '')
$pins = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-payloads.json') -Raw | ConvertFrom-Json
$root = [IO.Path]::GetFullPath($PayloadRoot)
$ancestor = Get-Item -LiteralPath $root
while ($null -ne $ancestor) {
    if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Payloads cannot traverse links.' }
    $ancestor = $ancestor.Parent
}
$payloads = @{}
foreach ($pin in $pins.payloads) {
    if ($pin.id -notin @('powershell', 'scoop', 'main') -or $pin.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Unexpected payload pin.' }
    $file = Join-Path $root ($pin.sha256 + '.zip')
    $item = Get-Item -LiteralPath $file
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint -or $item.Length -ne $pin.bytes -or
        (Get-FileHash -LiteralPath $file).Hash -ine $pin.sha256) { throw 'The served payload differs from its pin.' }
    $payloads['/' + $pin.id + '.zip'] = @{ path = $file; sha256 = $pin.sha256 }
}
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$origin = 'http://127.0.0.1:' + $port
[ordered]@{pid=$PID;startUtc=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o');port=$port;address='127.0.0.1';origin=$origin} |
    ConvertTo-Json | Set-Content -LiteralPath $ReadyPath -Encoding utf8
$deadline = [DateTime]::UtcNow.AddSeconds($LeaseSeconds)
$pending = $null
try {
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($null -eq $pending) { $pending = $listener.AcceptTcpClientAsync() }
        if (-not $pending.Wait(1000)) { continue }
        $client = $pending.GetAwaiter().GetResult(); $pending = $null
        $stream = $null; $reader = $null; $payload = $null
        try {
            $client.ReceiveTimeout = 5000; $client.SendTimeout = 30000
            $stream = $client.GetStream()
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
            $first = $reader.ReadLine()
            $headers = [Collections.Generic.List[string]]::new()
            for ($index = 0; $index -lt 100; $index++) {
                $line = $reader.ReadLine()
                if ([string]::IsNullOrEmpty($line)) { break }
                $headers.Add($line)
            }
            $parts = $first -split ' '
            $hostHeader = @($headers | Where-Object { $_ -match '^Host:' })
            $valid = $parts.Count -eq 3 -and $parts[0] -ceq 'GET' -and $parts[1].StartsWith('/') -and
                $hostHeader.Count -eq 1 -and $hostHeader[0] -ieq ('Host: 127.0.0.1:' + $port)
            $path = if ($valid) { $parts[1] } else { '<refused>' }
            $shutdown = $path -ceq '/__shutdown'
            $known = $shutdown -or $path -ceq '/metadata.json' -or $payloads.ContainsKey($path)
            $status = if (-not $valid) { 400 } elseif ($known) { 200 } else { 404 }
            $type = 'text/plain; charset=utf-8'
            $bytes = [Text.Encoding]::UTF8.GetBytes('Unknown or refused fixture route')
            if ($shutdown) { $bytes = [Text.Encoding]::UTF8.GetBytes('shutdown') }
            elseif ($path -ceq '/metadata.json') {
                $type = 'application/json'
                $bytes = [Text.Encoding]::UTF8.GetBytes('{"ReleaseTag":"v7.6.6","PreviewReleaseTag":"v7.6.6"}')
            } elseif ($valid -and $payloads.ContainsKey($path)) {
                $type = 'application/zip'
                $payload = [IO.File]::Open($payloads[$path].path, 'Open', 'Read', 'Read')
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $actual = [BitConverter]::ToString($sha.ComputeHash($payload)).Replace('-', '').ToLowerInvariant() }
                finally { $sha.Dispose() }
                if ($actual -cne $payloads[$path].sha256) { throw 'The payload changed before transmission.' }
                $payload.Position = 0
            }
            $length = if ($payload) { $payload.Length } else { $bytes.Length }
            [ordered]@{path=$path;status=$status;known=$known;bytes=$length} | ConvertTo-Json -Compress |
                Add-Content -LiteralPath $RequestLogPath -Encoding utf8
            $header = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status Fixture`r`nContent-Type: $type`r`nContent-Length: $length`r`nConnection: close`r`n`r`n")
            $stream.Write($header, 0, $header.Length)
            if ($payload) { $payload.CopyTo($stream) } else { $stream.Write($bytes, 0, $bytes.Length) }
            $stream.Flush()
            if ($shutdown) { break }
        } finally {
            if ($payload) { $payload.Dispose() }
            if ($reader) { $reader.Dispose() }
            if ($stream) { $stream.Dispose() }
            $client.Dispose()
        }
    }
} finally { $listener.Stop() }
