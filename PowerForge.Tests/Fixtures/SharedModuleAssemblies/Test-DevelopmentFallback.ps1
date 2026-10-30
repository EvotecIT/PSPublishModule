param([Parameter(Mandatory = $true)][string] $Root)
$ErrorActionPreference = 'Continue'
$env:PF_SHARED_FIXTURE_DEVELOPMENT = 'true'
Import-Module (Join-Path $Root 'Alpha/Alpha.psm1') -Force
$data = Get-AlphaData
if ($data.Feature -ne 1 -or $data.Private -ne 'Alpha-private') { throw 'Packaged fallback did not preserve the data host.' }
$loaded = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SharedCore' })
if ($loaded.Count -ne 1 -or $loaded[0].Location -like '*\bin\Release\*') {
    throw 'The incomplete development payload was used instead of the packaged payload.'
}
"DEVELOPMENT_FALLBACK_OK:$PSEdition"
