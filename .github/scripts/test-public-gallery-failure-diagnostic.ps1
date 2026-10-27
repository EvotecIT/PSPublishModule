param([ValidateSet('All', 'Deadline', 'Redaction', 'Redirect', 'Trace')][string]$Case = 'All')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
. (Join-Path $PSScriptRoot '../../PowerForge/Scripts/RepositoryDiagnostics/Collect-PublicGalleryFailure.ps1')

$script:repositoryUri = 'https://www.powershellgallery.com/api/v2'
$script:credentialInfo = $null
$script:repositoryReads = 0
function Get-PSResourceRepository {
    param($Name)
    $script:repositoryReads++
    [pscustomobject]@{ Uri = $script:repositoryUri; CredentialInfo = $script:credentialInfo }
}
function Get-Module {
    param($Name)
    [pscustomobject]@{ Version = [Version]'1.2.0'; Path = 'fixture/PSResourceGet.psd1' }
}
function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

if ($Case -in @('All', 'Trace') -and $PSVersionTable.PSVersion.Major -ge 7) {
    $savedFlag = $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS
    $savedRepositoryReads = $script:repositoryReads
    $trace = $null
    $listener = $null
    try {
        $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS = $null
        Assert-Contract ($null -eq (Start-PFPublicGalleryResponseTrace @('PSGallery') $false)) 'Opt-out must not subscribe to native responses.'
        $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS = '1'
        Assert-Contract ($null -eq (Start-PFPublicGalleryResponseTrace @('PSGallery') $true)) 'Explicit credentials must exclude native tracing.'
        Assert-Contract ($null -eq (Start-PFPublicGalleryResponseTrace @('Private') $false)) 'Private repositories must exclude native tracing.'
        $trace = Start-PFPublicGalleryResponseTrace @('PSGallery') $false
        Assert-Contract ($null -ne $trace) 'The supported opt-in host must start native observation.'
        $listener = [Diagnostics.DiagnosticListener]::new('HttpHandlerDiagnosticListener')
        if (-not ('PFNativeResponsePayload' -as [type])) {
            Add-Type 'public sealed class PFNativeResponsePayload { public System.Net.Http.HttpResponseMessage Response { get; set; } }'
        }
        $payload = [PFNativeResponsePayload]::new()
        foreach ($uri in @('https://private.example/api/v2/package/A/1.0.0', 'https://www.powershellgallery.com/private/token', 'https://www.powershellgallery.com/api/v2/package/A/1.0.0?token=do-not-copy')) {
            $response = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::NotFound)
            $response.RequestMessage = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $uri)
            $response.Headers.TryAddWithoutValidation('Set-Cookie', 'do-not-copy') | Out-Null
            $response.Headers.TryAddWithoutValidation('X-Cache', 'MISS') | Out-Null
            $response.Content = [Net.Http.StringContent]::new('private-body-do-not-copy')
            $payload.Response = $response
            $listener.Write('System.Net.Http.HttpRequestOut.Stop', $payload)
            $response.Dispose()
        }
        # Only the allowlisted endpoint metadata survives; queries, cookies and bodies never enter the snapshot.
        $records = @($trace.GetFailures())
        Assert-Contract ($records.Count -eq 1 -and $records[0].Status -eq 404) 'Only public endpoint failure metadata should be retained.'
        $snapshot = $records | ConvertTo-Json -Depth 5 -Compress
        Assert-Contract ($snapshot -notmatch 'do-not-copy|private.example|private-body|Set-Cookie|\?token') 'Native observation must exclude private paths, query strings, cookies and response bodies.'
        Assert-Contract ($records[0].Headers['X-Cache'] -eq 'MISS' -and $records[0].CollectionPhase -eq 'OriginalNativeHttpClientResponseStop') 'Original native response headers must stay distinguishable from separate controls.'
        $response = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::NotFound)
        $response.RequestMessage = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, 'https://www.powershellgallery.com/api/v2/package/A/1.0.0')
        $response.RequestMessage.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', 'do-not-copy')
        $payload.Response = $response
        $listener.Write('System.Net.Http.HttpRequestOut.Stop', $payload)
        Assert-Contract (@($trace.GetFailures()).Count -eq 1) 'Authenticated HTTP requests must be excluded even on a public host.'
        $response.RequestMessage.Headers.Authorization = $null
        for ($index = 0; $index -lt 6; $index++) { $listener.Write('System.Net.Http.HttpRequestOut.Stop', $payload) }
        Assert-Contract (@($trace.GetFailures()).Count -eq 4) 'Native response retention must be bounded.'
        $trace.Dispose()
        $listener.Write('System.Net.Http.HttpRequestOut.Stop', $payload)
        Assert-Contract (@($trace.GetFailures()).Count -eq 4) 'Disposal must detach the owned subscriptions.'
        $response.RequestMessage.Dispose()
        $response.Dispose()
        Write-Output 'Public Gallery native response trace: opt-in, privacy, original headers, bounds and disposal passed.'
    } finally {
        if ($trace) { $trace.Dispose() }
        if ($listener) { $listener.Dispose() }
        $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS = $savedFlag
        $script:repositoryReads = $savedRepositoryReads
    }
}

if ($Case -eq 'All') {
$savedFlag = $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS
$response = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::NotFound)
$response.RequestMessage = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, 'https://www.powershellgallery.com/api/v2/package/PSWriteColor/1.0.7')
$response.Content = [Net.Http.StringContent]::new(('token=fixture-secret ' + ('x' * 3000)))
$response.Headers.TryAddWithoutValidation('Set-Cookie', 'private-cookie') | Out-Null
$response.Headers.TryAddWithoutValidation('X-Cache', 'MISS') | Out-Null
$exception = [Exception]::new('Response404')
$exception | Add-Member -NotePropertyName Response -NotePropertyValue $response
$failure = [Management.Automation.ErrorRecord]::new($exception, 'Fixture404', [Management.Automation.ErrorCategory]::ResourceUnavailable, $null)
try {
    $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS = $null
    Assert-Contract (-not (Get-PFPublicGalleryFailureDiagnostic @('PSGallery') $failure $false)) 'Default path must be silent.'
    Assert-Contract ($script:repositoryReads -eq 0) 'Opt-out must not inspect registration.'
    $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS = '1'
    Assert-Contract (-not (Get-PFPublicGalleryFailureDiagnostic @('PSGallery') $failure $true)) 'Explicit credentials must disable collection.'
    Assert-Contract (-not (Get-PFPublicGalleryFailureDiagnostic @('Private') $failure $false)) 'Private repositories must be excluded.'
    Assert-Contract (-not (Get-PFPublicGalleryFailureDiagnostic @('PSGallery', 'Private') $failure $false)) 'Mixed repositories must be excluded.'
    $script:repositoryUri = 'https://private.example/v2'
    Assert-Contract (-not (Get-PFPublicGalleryFailureDiagnostic @('PSGallery') $failure $false)) 'A renamed private endpoint must be excluded.'
    $script:repositoryUri = 'https://www.powershellgallery.com/api/v2'
    $script:credentialInfo = [pscustomobject]@{ Name = 'fixture-vault-reference' }
    Assert-Contract (-not (Get-PFPublicGalleryFailureDiagnostic @('PSGallery') $failure $false)) 'Stored credential references must be excluded.'
    $script:credentialInfo = $null
    $diagnostic = Get-PFPublicGalleryFailureDiagnostic @('PSGallery') $failure $false
    $report = $diagnostic.Substring('PFPSRG-DIAGNOSTIC: '.Length) | ConvertFrom-Json
    Assert-Contract ($report.ModuleVersion -eq '1.2.0' -and $report.PowerShellVersion -eq $PSVersionTable.PSVersion.ToString()) 'Actual child runtime metadata must survive.'
    Assert-Contract ($report.OriginalResponse.Status -eq 404) 'Original HTTP status must survive.'
    Assert-Contract ($report.OriginalResponse.Headers.'X-Cache' -eq 'MISS') 'Allowlisted edge headers must survive.'
    Assert-Contract ($diagnostic -notmatch 'private-cookie|fixture-secret|Set-Cookie') 'Diagnostic must omit cookie headers and redact labeled secrets.'
    Assert-Contract ($report.OriginalResponse.ErrorBody.Length -le 1024) 'Error body must stay bounded.'
    Assert-Contract ($null -eq $report.PostFailureControl) 'An available original response must not trigger another request.'
    $response.RequestMessage.RequestUri = [Uri]'https://private.example/error'
    $response.Content.Dispose()
    $response.Content = [Net.Http.StringContent]::new('private-response-do-not-read')
    $excluded = Get-PFPublicGalleryFailureDiagnostic @('PSGallery') $failure $false
    $excludedReport = $excluded.Substring('PFPSRG-DIAGNOSTIC: '.Length) | ConvertFrom-Json
    Assert-Contract ($excludedReport.OriginalResponse.BodyStatus -eq 'NotCollectedForUnrecognizedEndpoint') 'A redirected or private response must be excluded.'
    Assert-Contract ($excluded -notmatch 'private-response-do-not-read|private.example') 'Excluded response details must not leak.'
    foreach ($uri in @('https://private.example/api/v2/package/A/1.0.0', 'https://user:secret@www.powershellgallery.com/api/v2/package/A/1.0.0', 'https://www.powershellgallery.com:8443/api/v2/package/A/1.0.0', 'https://www.powershellgallery.com/api/v2/package/A/1.0.0?token=secret')) {
        Assert-Contract ($null -eq (ConvertTo-PFPublicGalleryUri $uri)) 'Unsafe control URI must be rejected.'
    }
    $query = 'https://www.powershellgallery.com/api/v2/FindPackagesById()?%24filter=Id+eq+%27PSSharedGoods%27+and+NormalizedVersion+eq+%270.0.313%27&%24inlinecount=allpages&id=%27PSSharedGoods%27'
    Assert-Contract ($null -ne (ConvertTo-PFPublicGalleryUri $query)) 'Observed public Gallery query must be accepted.'
    Assert-Contract ($null -eq (ConvertTo-PFPublicGalleryUri ($query + '&token=secret'))) 'Extra query fields must be rejected.'
    Write-Output 'Public Gallery failure diagnostics: runtime, original response, bounds, redaction and opt-in boundaries passed.'
} finally {
    $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS = $savedFlag
    $response.RequestMessage.Dispose()
    $response.Dispose()
}
}

if ($Case -in @('All', 'Redaction')) {
    $response = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::NotFound)
    try {
        $response.Content = [Net.Http.StringContent]::new(@'
public-error
Authorization: Basic fixture-basic
Authorization: Digest username="fixture-user", response="fixture-digest"
Authorization: Negotiate fixture-negotiate
Authorization: NTLM fixture-ntlm
{"Authorization":"Basic fixture-json"}
{"Proxy-Authorization":"Digest username=\"fixture-proxy-user\", response=\"fixture-proxy\""}
'@)
        $result = Get-PFPublicGalleryResponse $response
        Assert-Contract ($result.ErrorBody -notmatch 'fixture-') 'Authorization credentials must be completely redacted for every supported scheme.'
        Assert-Contract ($result.ErrorBody -match 'public-error' -and $result.ErrorBody -match '\[REDACTED\]') 'Redaction must preserve useful public error text.'
    } finally { $response.Dispose() }
}

if ($Case -in @('All', 'Deadline')) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
public sealed class GalleryNonCancellingStream : Stream {
    public bool WasDisposed { get; private set; }
    public override bool CanRead { get { return true; } }
    public override bool CanSeek { get { return false; } }
    public override bool CanWrite { get { return false; } }
    public override long Length { get { throw new NotSupportedException(); } }
    public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) {
        return Task.Delay(6000).ContinueWith(task => 0);
    }
    protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
    public override void SetLength(long value) { throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
}
'@
    $stream = [GalleryNonCancellingStream]::new()
    $response = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::NotFound)
    $response.Content = [Net.Http.StreamContent]::new($stream)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $result = Get-PFPublicGalleryResponse $response
        Assert-Contract ($result.BodyStatus -eq 'TimedOut' -and $stream.WasDisposed) 'A read that ignores cancellation must time out and release its stream.'
        Assert-Contract ($watch.ElapsedMilliseconds -lt 5500) 'The independent deadline must beat the six-second non-canceling read.'
    } finally { $response.Dispose() }
}

if ($Case -in @('All', 'Redirect')) {
    $cdn = 'https://cdn.powershellgallery.com/packages/pssharedgoods.0.0.313.nupkg'
    Assert-Contract ($null -ne (ConvertTo-PFPublicGalleryUri $cdn)) 'The observed public CDN package route must be accepted.'
    foreach ($uri in @($cdn + '?token=secret', 'https://cdn.powershellgallery.com/private', 'https://cdn.powershellgallery.com.evil/packages/a.1.0.0.nupkg')) {
        Assert-Contract ($null -eq (ConvertTo-PFPublicGalleryUri $uri)) 'Unrecognized CDN routes and query data must be rejected.'
    }
    $script:controlRequests = [Collections.Generic.List[string]]::new()
    function Invoke-PFPublicGalleryControl {
        param([Uri]$Uri)
        $script:controlRequests.Add($Uri.AbsoluteUri)
        if ($Uri.Host -eq 'www.powershellgallery.com') {
            return @{ Status = 302; CdnRedirectUri = $cdn; CollectionPhase = 'SeparateUnauthenticatedHttpClientAfterFailure' }
        }
        # Even a second redirect must not cause a third request.
        return @{ Status = 302; CdnRedirectUri = $cdn; CollectionPhase = 'SeparateUnauthenticatedHttpClientAfterFailure' }
    }
    $savedFlag = $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS
    try {
        $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS = '1'
        $script:repositoryUri = 'https://www.powershellgallery.com/api/v2'
        $script:credentialInfo = $null
        $exception = [Exception]::new("Request sent: 'https://www.powershellgallery.com/api/v2/package/PSSharedGoods/0.0.313'")
        $failure = [Management.Automation.ErrorRecord]::new($exception, 'FixtureRedirect', [Management.Automation.ErrorCategory]::ResourceUnavailable, $null)
        $text = Get-PFPublicGalleryFailureDiagnostic @('PSGallery') $failure $false
        $report = $text.Substring('PFPSRG-DIAGNOSTIC: '.Length) | ConvertFrom-Json
        Assert-Contract ($script:controlRequests.Count -eq 2 -and $script:controlRequests[1] -eq $cdn) 'The control chain must stop after its single allowlisted CDN hop.'
        Assert-Contract ($report.OriginalResponse.BodyStatus -eq 'NotExposedByBackend' -and $report.CdnControl.CollectionPhase -eq 'SeparateUnauthenticatedHttpClientAfterFailure') 'Separate controls must not be presented as the original response.'
    } finally { $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS = $savedFlag }
}
Write-Output "Requested public Gallery diagnostic contracts passed: $Case"
