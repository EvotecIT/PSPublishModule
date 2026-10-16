[CmdletBinding()]
param([string] $RoutesPath, [string] $ReadyPath, [string] $RequestLogPath, [int] $Port = 0, [ValidateRange(30,3600)][int] $LeaseSeconds = 3600, [string] $DiagnosticRoot)
$ErrorActionPreference = 'Stop'
if ($DiagnosticRoot) {
    [IO.File]::WriteAllText((Join-Path $DiagnosticRoot 'server.stdout.txt'), '')
    [IO.File]::WriteAllText((Join-Path $DiagnosticRoot 'server.stderr.txt'), '')
}
trap {
    if ($DiagnosticRoot) { [IO.File]::WriteAllText((Join-Path $DiagnosticRoot 'server.stderr.txt'), ($_ | Out-String)) }
    else { [Console]::Error.WriteLine($_.ToString()) }
    exit 1
}
$routes = Get-Content -LiteralPath $RoutesPath -Raw | ConvertFrom-Json
if(@($routes | Where-Object {$_.status -ge 300 -and $_.status -lt 400}).Count -ne 0){throw 'The owned fixture refuses HTTP redirects; no Location response may escape loopback.'}
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
$listener.Start()
$actualPort = ([Net.IPEndPoint] $listener.LocalEndpoint).Port
$origin = 'http://127.0.0.1:' + $actualPort
$identity = [ordered]@{ Pid=$PID; StartUtc=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o'); Port=$actualPort; Address='127.0.0.1'; WorkingDirectory=(Get-Location).Path; Origin=$origin }
$identity | ConvertTo-Json | Set-Content -LiteralPath $ReadyPath -Encoding utf8NoBOM
$deadline = [DateTime]::UtcNow.AddSeconds($LeaseSeconds)
$pending = $null
try {
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($null -eq $pending) { $pending = $listener.AcceptTcpClientAsync() }
        if (-not $pending.Wait(1000)) { continue }
        $client = $pending.GetAwaiter().GetResult(); $pending = $null
        $stream = $null; $reader = $null
        try {
            $client.ReceiveTimeout=5000; $client.SendTimeout=5000
            $stream=$client.GetStream()
            $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::ASCII,$false,1024,$true)
            $first=$reader.ReadLine()
            $headers=[Collections.Generic.List[string]]::new()
            for($index=0;$index -lt 100;$index++) { $line=$reader.ReadLine(); if([string]::IsNullOrEmpty($line)){break}; $headers.Add($line) }
            $parts=$first -split ' '
            $hostLine=@($headers | Where-Object { $_ -match '^Host:' })
            $expectedHost='Host: 127.0.0.1:' + $actualPort
            $valid=$parts.Count -eq 3 -and $parts[0] -eq 'GET' -and $parts[1].StartsWith('/') -and $hostLine.Count -eq 1 -and $hostLine[0] -ieq $expectedHost
            $path=if($valid){([Uri]($origin + $parts[1])).AbsolutePath}else{'<refused>'}
            $shutdown=$valid -and $path -eq '/__shutdown'
            $route=if($valid -and -not $shutdown){@($routes | Where-Object path -CEQ $path)}else{@()}
            $status=if(-not $valid){400}elseif($shutdown){200}elseif($route.Count -eq 1){[int]$route[0].status}else{404}
            $body=if($shutdown){'shutdown'}elseif($route.Count -eq 1){[string]$route[0].body}else{'Unknown or refused fixture route'}
            $body=$body.Replace('{ORIGIN}',$origin)
            $type=if($route.Count -eq 1){[string]$route[0].contentType}else{'text/plain; charset=utf-8'}
            [ordered]@{path=$path;status=$status;accepted=$valid;known=($route.Count -eq 1 -or $shutdown);utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Compress | Add-Content -LiteralPath $RequestLogPath -Encoding utf8NoBOM
            $bytes=[Text.Encoding]::UTF8.GetBytes($body)
            $header=[Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status Fixture`r`nContent-Type: $type`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n")
            $stream.Write($header,0,$header.Length); $stream.Write($bytes,0,$bytes.Length); $stream.Flush()
            if($shutdown){break}
        } finally {
            if($null -ne $reader){$reader.Dispose()}
            if($null -ne $stream){$stream.Dispose()}
            $client.Dispose()
        }
    }
} finally { $listener.Stop() }
