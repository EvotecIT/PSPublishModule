# Opt-in failure evidence for unauthenticated public Gallery reads only.
$script:PFPublicGalleryHeaderCredentialPattern = '(?i)\b(?:Basic|Bearer|Digest|Negotiate|NTLM)\s+\S+|(?:token|secret|password|api[-_]?key|(?:proxy[-_]?)?authorization|cookie)["''\\]*\s*[:=]'
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
            if ([regex]::IsMatch($value, $script:PFPublicGalleryHeaderCredentialPattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant, [TimeSpan]::FromMilliseconds(100))) { $value = '[REDACTED]' }
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

function Test-PFPublicGalleryDiagnosticAllowed {
    param([string[]]$Repositories, [bool]$HasCredential)
    if ($env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS -ne '1' -or $HasCredential -or $Repositories.Count -ne 1 -or $Repositories[0] -ne 'PSGallery') { return $false }
    try {
        $repository = Get-PSResourceRepository -Name PSGallery -ErrorAction Stop
        return [string]$repository.Uri -match '^https://www\.powershellgallery\.com/api/v2/?$' -and
            -not ($repository.PSObject.Properties['CredentialInfo'] -and $repository.CredentialInfo) -and
            -not ($repository.PSObject.Properties['CredentialProvider'] -and $repository.CredentialProvider)
    } catch { return $false }
}

function Start-PFPublicGalleryResponseTrace {
    param([string[]]$Repositories, [bool]$HasCredential)
    if ($PSVersionTable.PSVersion.Major -lt 7 -or -not (Test-PFPublicGalleryDiagnosticAllowed $Repositories $HasCredential)) { return $null }
    try {
        if (-not ('PFPublicGalleryResponseTrace' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
public sealed class PFPublicGalleryResponseTrace : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string,object>>, IDisposable {
    readonly object gate = new object();
    readonly List<IDisposable> subscriptions = new List<IDisposable>();
    readonly List<Dictionary<string,object>> failures = new List<Dictionary<string,object>>();
    readonly Regex headerCredentialPattern;
    bool disposed;
    public PFPublicGalleryResponseTrace(string credentialPattern) {
        headerCredentialPattern = new Regex(credentialPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        lock (gate) subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
    }
    public void OnNext(DiagnosticListener listener) {
        if (listener.Name != "HttpHandlerDiagnosticListener") return;
        lock (gate) { if (!disposed) subscriptions.Add(listener.Subscribe(this)); }
    }
    public void OnNext(KeyValuePair<string,object> item) {
        if (item.Key != "System.Net.Http.HttpRequestOut.Stop" || item.Value == null) return;
        try {
            var property = item.Value.GetType().GetProperty("Response");
            var response = property == null ? null : property.GetValue(item.Value) as HttpResponseMessage;
            var request = response == null ? null : response.RequestMessage;
            var uri = request == null ? null : request.RequestUri;
            if (uri == null || response.IsSuccessStatusCode || (int)response.StatusCode < 400 ||
                uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                request.Headers.Authorization != null || request.Headers.Contains("Proxy-Authorization") || request.Headers.Contains("Cookie")) return;
            bool api = uri.Host == "www.powershellgallery.com" && (uri.AbsolutePath == "/api/v2/FindPackagesById()" ||
                Regex.IsMatch(uri.AbsolutePath, @"^/api/v2/package/[A-Za-z0-9_.-]+/[A-Za-z0-9_.+-]+$"));
            bool cdn = uri.Host == "cdn.powershellgallery.com" && Regex.IsMatch(uri.AbsolutePath, @"^/packages/[A-Za-z0-9_.+-]+\.nupkg$");
            if (!api && !cdn) return;
            var headers = new Dictionary<string,string>();
            foreach (var name in new [] {"Date", "Server", "X-Cache", "X-Azure-Ref", "Retry-After", "X-Ms-Error-Code"}) {
                IEnumerable<string> values;
                if (!response.Headers.TryGetValues(name, out values)) continue;
                string value = string.Join(", ", values).Replace("\r", " ").Replace("\n", " ");
                headers[name] = headerCredentialPattern.IsMatch(value) ? "[REDACTED]" : value.Substring(0, Math.Min(256, value.Length));
            }
            var record = new Dictionary<string,object> {
                {"Status", (int)response.StatusCode}, {"Headers", headers},
                {"Endpoint", uri.GetLeftPart(UriPartial.Path)}, {"RequestQuery", "NotCollected"},
                {"BodyStatus", "NotReadByDiagnosticListener"}, {"CollectionPhase", "OriginalNativeHttpClientResponseStop"}
            };
            lock (gate) { if (!disposed) { if (failures.Count == 4) failures.RemoveAt(0); failures.Add(record); } }
        } catch { }
    }
    public Dictionary<string,object>[] GetFailures() { lock (gate) return failures.ToArray(); }
    public void OnError(Exception error) { }
    public void OnCompleted() { }
    public void Dispose() {
        IDisposable[] owned;
        lock (gate) { if (disposed) return; disposed = true; owned = subscriptions.ToArray(); subscriptions.Clear(); }
        foreach (var value in owned) value.Dispose();
    }
}
'@
        }
        return [PFPublicGalleryResponseTrace]::new($script:PFPublicGalleryHeaderCredentialPattern)
    } catch { return $null }
}

function Get-PFPublicGalleryFailureDiagnostic {
    param([string[]]$Repositories, [Management.Automation.ErrorRecord]$Failure, [bool]$HasCredential, [object]$Trace = $null)
    if (-not (Test-PFPublicGalleryDiagnosticAllowed $Repositories $HasCredential)) { return '' }
    try {
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
            NativeResponseTrace = if ($Trace) { 'Enabled' } elseif ($PSVersionTable.PSVersion.Major -lt 7) { 'UnsupportedHost' } else { 'Unavailable' }
            NativeFailureResponses = @(if ($Trace) { $Trace.GetFailures() })
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
