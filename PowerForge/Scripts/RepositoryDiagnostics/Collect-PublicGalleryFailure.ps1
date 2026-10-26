# Opt-in failure evidence for unauthenticated public Gallery reads only.
function ConvertTo-PFPublicGalleryUri {
    param([string]$Value)
    $uri = $null
    if ($Value.Length -gt 2048 -or -not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri)) { return $null }
    if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('www.powershellgallery.com', 'cdn.powershellgallery.com') -or -not $uri.IsDefaultPort -or $uri.UserInfo -or $uri.Fragment) { return $null }
    if ($uri.Host -eq 'cdn.powershellgallery.com') {
        if ($uri.AbsolutePath -match '^/packages/[A-Za-z0-9_.+-]+\.nupkg$' -and -not $uri.Query) { return $uri }
        return $null
    }
    if ($uri.AbsolutePath -match '^/api/v2/package/[A-Za-z0-9_.-]+/[A-Za-z0-9_.+-]+$' -and -not $uri.Query) { return $uri }
    if ($uri.AbsolutePath -ne '/api/v2/FindPackagesById()') { return $null }
    $query = @{}
    foreach ($part in $uri.Query.TrimStart('?').Split('&')) {
        $pair = $part.Split('=', 2)
        if ($pair.Count -ne 2) { return $null }
        $key = [Uri]::UnescapeDataString($pair[0])
        $value = [Uri]::UnescapeDataString($pair[1].Replace('+', ' '))
        if ($key -notin @('$filter', '$inlinecount', 'id') -or $query.ContainsKey($key)) { return $null }
        $query[$key] = $value
    }
    if ($query.Count -ne 3 -or $query['$inlinecount'] -ne 'allpages' -or $query['id'] -notmatch "^'[A-Za-z0-9_.-]+'$") { return $null }
    if ($query['$filter'] -notmatch "^Id eq '[A-Za-z0-9_.-]+' and (NormalizedVersion eq '[A-Za-z0-9_.+-]+'|IsLatestVersion eq true)$") { return $null }
    return $uri
}

function Get-PFPublicGalleryResponse {
    param([Net.Http.HttpResponseMessage]$Response)
    $headers = @{}
    foreach ($header in @($Response.Headers) + @($Response.Content.Headers)) {
        if ($header.Key -in @('Date', 'Server', 'Content-Type', 'Content-Length', 'Cache-Control', 'ETag', 'Retry-After', 'X-Cache', 'X-Azure-Ref', 'X-Ms-Error-Code')) {
            $value = ([string]::Join(', ', [string[]]$header.Value) -replace '[\r\n]', ' ')
            $headers[$header.Key] = $value.Substring(0, [Math]::Min(256, $value.Length))
        }
    }
    $body = ''
    $bodyStatus = 'NotReadForSuccessfulResponse'
    if (-not $Response.IsSuccessStatusCode) {
        $bodyStatus = 'Unavailable'
        $timeout = [Threading.CancellationTokenSource]::new(3000)
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $stream = $null
        try {
            $open = $Response.Content.ReadAsStreamAsync()
            if (-not $open.Wait(3000)) { $Response.Content.Dispose(); throw [TimeoutException]::new() }
            $stream = $open.GetAwaiter().GetResult()
            $remaining = [int][Math]::Max(0, 3000 - $watch.ElapsedMilliseconds)
            if ($remaining -eq 0) { $stream.Dispose(); throw [TimeoutException]::new() }
            $buffer = [byte[]]::new(1024)
            $read = $stream.ReadAsync($buffer, 0, $buffer.Length, $timeout.Token)
            # Legacy streams can ignore cancellation after starting a read.
            if (-not $read.Wait($remaining)) { $stream.Dispose(); throw [TimeoutException]::new() }
            $count = $read.GetAwaiter().GetResult()
            $body = [Text.Encoding]::UTF8.GetString($buffer, 0, $count) -replace '[\x00-\x08\x0B\x0C\x0E-\x1F]', ''
            $body = $body -replace '(?i)(bearer\s+)[A-Za-z0-9._~+/-]+=*', '$1[REDACTED]'
            $body = $body -replace '(?i)("(?:proxy-)?authorization"\s*:\s*")(?:\\.|[^"\\])*', '$1[REDACTED]'
            $body = $body -replace '(?im)(\b(?:proxy-)?authorization\s*[:=]\s*)[^\r\n<>]+', '$1[REDACTED]'
            $body = $body -replace '(?i)((?:token|secret|password|api[-_]?key|authorization|cookie)["'']?\s*[=:]\s*["'']?)[^\s<>"'']+', '$1[REDACTED]'
            $body = $body.Substring(0, [Math]::Min(1024, $body.Length))
            $bodyStatus = 'BoundedTo1024Bytes'
        } catch [TimeoutException] { $body = ''; $bodyStatus = 'TimedOut' }
        catch { $body = '' } finally { $timeout.Dispose() }
    }
    return @{ Status = [int]$Response.StatusCode; Headers = $headers; ErrorBody = $body; BodyStatus = $bodyStatus }
}

function Invoke-PFPublicGalleryControl {
    param([Uri]$Uri)
    $safeUri = ConvertTo-PFPublicGalleryUri ([string]$Uri)
    if (-not $safeUri) { return $null }
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $handler.UseDefaultCredentials = $false
    $handler.MaxResponseHeadersLength = 16
    $client = [Net.Http.HttpClient]::new($handler)
    $timeout = [Threading.CancellationTokenSource]::new(5000)
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $safeUri)
    $response = $null
    try {
        $pending = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $timeout.Token)
        if (-not $pending.Wait(5000)) { throw [TimeoutException]::new() }
        $response = $pending.GetAwaiter().GetResult()
        $result = Get-PFPublicGalleryResponse $response
        $result.Uri = $safeUri.AbsoluteUri
        $result.CollectionPhase = 'SeparateUnauthenticatedHttpClientAfterFailure'
        $redirect = ConvertTo-PFPublicGalleryUri ([string]$response.Headers.Location)
        if ($safeUri.Host -eq 'www.powershellgallery.com' -and $result.Status -in @(301, 302, 303, 307, 308) -and $redirect -and $redirect.Host -eq 'cdn.powershellgallery.com') {
            $result.CdnRedirectUri = $redirect.AbsoluteUri
        }
        return $result
    } catch { return @{ CollectionPhase = 'SeparateUnauthenticatedHttpClientAfterFailure'; Result = 'Unavailable' } }
    finally {
        if ($response) { $response.Dispose() }
        $request.Dispose(); $timeout.Dispose(); $client.Dispose()
    }
}

function Get-PFPublicGalleryFailureDiagnostic {
    param([string[]]$Repositories, [Management.Automation.ErrorRecord]$Failure, [bool]$HasCredential)
    if ($env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS -ne '1' -or $HasCredential -or $Repositories.Count -ne 1 -or $Repositories[0] -ne 'PSGallery') { return '' }
    try {
        $repository = Get-PSResourceRepository -Name PSGallery -ErrorAction Stop
        if ([string]$repository.Uri -notmatch '^https://www\.powershellgallery\.com/api/v2/?$') { return '' }
        if (($repository.PSObject.Properties['CredentialInfo'] -and $repository.CredentialInfo) -or ($repository.PSObject.Properties['CredentialProvider'] -and $repository.CredentialProvider)) { return '' }
        $module = Get-Module Microsoft.PowerShell.PSResourceGet | Select-Object -First 1
        $report = [ordered]@{
            PowerShellVersion = $PSVersionTable.PSVersion.ToString()
            DotNetVersion = [Environment]::Version.ToString()
            Executable = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
            ModuleVersion = if ($module) { $module.Version.ToString() } else { $null }
            ModulePath = if ($module) { $module.Path } else { $null }
            RepositoryUri = 'https://www.powershellgallery.com/api/v2'
            ErrorType = $Failure.Exception.GetType().FullName
            OriginalResponse = @{ BodyStatus = 'NotExposedByBackend' }
            PostFailureControl = $null
            CdnControl = $null
        }
        $response = $null
        $exception = $Failure.Exception
        for ($depth = 0; $exception -and $depth -lt 5; $depth++) {
            if ($exception.PSObject.Properties['Response'] -and $exception.Response -is [Net.Http.HttpResponseMessage]) {
                $response = $exception.Response
                break
            }
            $exception = $exception.InnerException
        }
        if ($response) {
            $uri = ConvertTo-PFPublicGalleryUri ([string]$response.RequestMessage.RequestUri)
            if ($uri) {
                try { $report.OriginalResponse = Get-PFPublicGalleryResponse $response } catch { }
                $report.OriginalResponse.Uri = $uri.AbsoluteUri
            } else {
                $report.OriginalResponse = @{ Status = [int]$response.StatusCode; BodyStatus = 'NotCollectedForUnrecognizedEndpoint' }
            }
        }
        # PSResourceGet can discard the original response. A separate GET is
        # explicitly labeled a control and never retries the failed operation.
        if (-not $response) {
            $match = [regex]::Match($Failure.Exception.Message, "https://(?:www|cdn)\.powershellgallery\.com/(?:api/v2/|packages/)[^\s'\""<>]+")
            $uri = ConvertTo-PFPublicGalleryUri $match.Value
            if ($uri) {
                $report.PostFailureControl = Invoke-PFPublicGalleryControl $uri
                if ($report.PostFailureControl.CdnRedirectUri) {
                    $report.CdnControl = Invoke-PFPublicGalleryControl ([Uri]$report.PostFailureControl.CdnRedirectUri)
                }
            }
        }
        return 'PFPSRG-DIAGNOSTIC: ' + ($report | ConvertTo-Json -Depth 6 -Compress)
    } catch { return 'PFPSRG-DIAGNOSTIC: {"CollectionStatus":"Unavailable"}' }
}
