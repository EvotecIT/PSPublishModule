function Invoke-ImportValidation {
    param([string] $OutputRoot)

    if (-not $ValidateImport.IsPresent -or [string]::IsNullOrWhiteSpace($OutputRoot) -or -not (Test-Path -LiteralPath $OutputRoot)) {
        return $null
    }

    $childScript = Join-Path $PSScriptRoot 'Invoke-ManagedModuleImportChild.ps1'
    $output = @(& (Get-BenchmarkHostPath) -NoLogo -NoProfile -ExecutionPolicy Bypass -File $childScript -ModuleName $ModuleName -ModuleRoot $OutputRoot 2>&1)
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{
            Status = 'Failed'
            Version = ''
            ManifestPath = ''
            ElapsedMilliseconds = 0
            Error = ($output -join [Environment]::NewLine)
        }
    }

    $json = @($output | Where-Object { [string] $_ -match '^\s*\{' } | Select-Object -Last 1)
    if ($json.Count -eq 0) {
        return [pscustomobject]@{
            Status = 'Failed'
            Version = ''
            ManifestPath = ''
            ElapsedMilliseconds = 0
            Error = ($output -join [Environment]::NewLine)
        }
    }

    $json[0] | ConvertFrom-Json
}
