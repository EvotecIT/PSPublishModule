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
