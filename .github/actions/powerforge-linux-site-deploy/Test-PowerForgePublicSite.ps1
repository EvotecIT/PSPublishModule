function Invoke-PowerForgePublicRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Uri] $Uri,
        [int] $MaximumAttempts = 4
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try {
            return Invoke-WebRequest -Uri $Uri -Method Get -TimeoutSec 30 -MaximumRedirection 5 -ErrorAction Stop
        } catch {
            $lastError = $_
            if ($attempt -lt $MaximumAttempts) {
                Start-Sleep -Seconds ([math]::Pow(2, $attempt - 1))
            }
        }
    }

    throw "Public website request failed after $MaximumAttempts attempts: $Uri. $($lastError.Exception.Message)"
}

function New-PowerForgeSmokeUri {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Uri] $BaseUri,
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $RunId,
        [Parameter(Mandatory)][string] $RunAttempt
    )

    if (-not $Path.StartsWith('/', [StringComparison]::Ordinal) -or $Path.Contains('?') -or $Path.Contains('#')) {
        throw "Public smoke path must be an absolute path without a query or fragment: $Path"
    }

    $builder = [UriBuilder]::new([Uri]::new($BaseUri, $Path))
    $builder.Query = "powerforge-deploy=$RunId-$RunAttempt"
    return $builder.Uri
}

function Assert-PowerForgePublicSite {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Uri] $BaseUri,
        [Parameter(Mandatory)][string[]] $SmokePaths,
        [Parameter(Mandatory)][string] $SourceSha,
        [Parameter(Mandatory)][string] $ArtifactSha256,
        [Parameter(Mandatory)][string] $RunId,
        [Parameter(Mandatory)][string] $RunAttempt
    )

    $markerUri = New-PowerForgeSmokeUri -BaseUri $BaseUri -Path '/_powerforge/deployment.json' -RunId $RunId -RunAttempt $RunAttempt
    $markerResponse = Invoke-PowerForgePublicRequest -Uri $markerUri
    try {
        $marker = $markerResponse.Content | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "Public deployment marker is not valid JSON: $markerUri. $($_.Exception.Message)"
    }

    $expected = [ordered]@{
        sourceSha          = $SourceSha
        artifactSha256     = $ArtifactSha256
        workflowRunId      = $RunId
        workflowRunAttempt = $RunAttempt
    }
    foreach ($entry in $expected.GetEnumerator()) {
        if ([string]$marker.($entry.Key) -ne [string]$entry.Value) {
            throw "Public deployment marker has unexpected $($entry.Key): expected '$($entry.Value)', observed '$($marker.($entry.Key))'."
        }
    }

    foreach ($path in $SmokePaths) {
        $smokeUri = New-PowerForgeSmokeUri -BaseUri $BaseUri -Path $path -RunId $RunId -RunAttempt $RunAttempt
        $null = Invoke-PowerForgePublicRequest -Uri $smokeUri
    }
}
