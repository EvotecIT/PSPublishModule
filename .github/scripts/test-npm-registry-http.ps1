param(
    [Parameter(Mandatory)][string] $TransportPath,
    [Parameter(Mandatory)][string] $OutputPath
)
$ErrorActionPreference = 'Stop'
. $TransportPath
$results = [Collections.Generic.List[object]]::new()
foreach ($mode in @('complete', 'stalled', 'trickling')) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $ready = [Threading.ManualResetEventSlim]::new($false)
    $headersSent = [Threading.ManualResetEventSlim]::new($false)
    $worker = $null
    $listener.Start()
    try {
        $port = $listener.LocalEndpoint.Port
        $worker = Start-ThreadJob -ArgumentList $listener, $ready, $headersSent, $mode -ScriptBlock {
            param($Listener, $Ready, $HeadersSent, $Mode)
            $client = $null
            $reader = $null
            try {
                $Ready.Set()
                $client = $Listener.AcceptTcpClient()
                $stream = $client.GetStream()
                $reader = [IO.StreamReader]::new($stream)
                while ($reader.ReadLine()) { }
                $body = if ($Mode -eq 'complete') { '{"verified":true}' } else { '.' * 200 }
                $headers = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
                $stream.Write($headers, 0, $headers.Length)
                $stream.Flush()
                $HeadersSent.Set()
                if ($Mode -eq 'complete') {
                    $bytes = [Text.Encoding]::ASCII.GetBytes($body)
                    $stream.Write($bytes, 0, $bytes.Length)
                    $stream.Flush()
                } elseif ($Mode -eq 'stalled') {
                    Start-Sleep -Seconds 20
                } else {
                    foreach ($character in $body.ToCharArray()) {
                        $stream.WriteByte([byte][char]$character)
                        $stream.Flush()
                        Start-Sleep -Milliseconds 100
                    }
                }
            } catch {
                # The timed-out client closes its socket while the server is still writing.
            } finally {
                if ($reader) { $reader.Dispose() }
                if ($client) { $client.Dispose() }
            }
        }
        if (-not $ready.Wait(5000)) { throw 'The loopback HTTP worker did not start.' }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $failed = $false
        $message = ''
        try { $response = Invoke-NpmRegistryRequest -Url "http://127.0.0.1:$port/metadata" -TimeoutSeconds 1 }
        catch { $failed = $true; $message = $_.Exception.Message }
        $clock.Stop()
        if (-not $headersSent.IsSet) { throw 'The deadline test never received response headers.' }
        if ($mode -eq 'complete') {
            if ($failed -or $response.StatusCode -ne 200 -or $response.Content -cne '{"verified":true}') { throw 'The real transport did not return complete registry metadata.' }
        } elseif (-not $failed -or $clock.Elapsed.TotalSeconds -ge 10 -or $message -notmatch 'cancel|timeout') {
            throw "The real $mode response was not stopped by the total request deadline: $message"
        }
        $results.Add([pscustomobject]@{case=$mode;headersReceived=$headersSent.IsSet;failed=$failed;elapsedSeconds=$clock.Elapsed.TotalSeconds;message=$message})
    } finally {
        $listener.Stop()
        if ($worker) { Stop-Job $worker; Remove-Job $worker }
        $ready.Dispose()
        $headersSent.Dispose()
    }
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath
Write-Output 'Three real loopback HTTP transport contracts passed; listeners and worker jobs closed.'
