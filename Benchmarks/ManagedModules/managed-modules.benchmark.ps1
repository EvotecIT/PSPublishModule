#requires -Version 5.1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$moduleScenarios = @(
    [pscustomobject]@{ Name = 'SingleModule'; ModuleName = 'PSScriptAnalyzer'; Version = '1.25.0'; AcceptLicense = $false }
    [pscustomobject]@{ Name = 'GraphAuthentication'; ModuleName = 'Microsoft.Graph.Authentication'; Version = '2.29.1'; AcceptLicense = $false }
    [pscustomobject]@{ Name = 'Graph'; ModuleName = 'Microsoft.Graph'; Version = '2.29.1'; AcceptLicense = $false }
    [pscustomobject]@{ Name = 'AzAccounts'; ModuleName = 'Az.Accounts'; Version = '5.1.0'; AcceptLicense = $true }
    [pscustomobject]@{ Name = 'Az'; ModuleName = 'Az'; Version = '14.0.0'; AcceptLicense = $true }
)

benchmark 'managed-modules' -out (Join-Path $repositoryRoot 'Ignore\Benchmarks\ManagedModules') {
    profile Current -Cleanup KeepOnFailure
    caseSource $moduleScenarios
    axis Host Current

    setup {
        param($case, $run)

        $run.RepositoryName = 'PSGallery'
        $run.RepositoryUri = 'https://www.powershellgallery.com/api/v3/index.json'
        $run.ModuleFastSource = 'https://pwsh.gallery/index.json'
        $run.ModuleFastModulePath = ''
        $run.InstallRoot = Join-Path $run.OutputDirectory 'installed'
        $run.SaveRoot = Join-Path $run.OutputDirectory 'saved'
        $run.PackageCacheRoot = Join-Path $run.OutputDirectory 'package-cache'

        New-Item -ItemType Directory -Path $run.InstallRoot, $run.SaveRoot, $run.PackageCacheRoot -Force | Out-Null
    }

    skip {
        param($case)

        if ([string] $case.Engine -eq 'ModuleFast' -and [string] $case.Operation -ne 'Install') {
            return $true
        }

        if ([string] $case.Profile -ne 'TemporaryLocalUser' -and
            [string] $case.Operation -eq 'Install' -and
            @('PSResourceGet', 'PowerShellGet') -contains [string] $case.Engine) {
            return $true
        }

        return $false
    }

    engine Managed {
        operation Find {
            param($case, $run)

            Find-ManagedModule -Name $case.ModuleName -Repository $run.RepositoryUri -RepositoryName $run.RepositoryName | Out-Null
        }

        operation Install {
            param($case, $run)

            $run.ManagedResult = Install-ManagedModule `
                -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.RepositoryUri `
                -RepositoryName $run.RepositoryName `
                -Scope Custom `
                -ModuleRoot $run.InstallRoot `
                -PackageCacheDirectory $run.PackageCacheRoot `
                -AcceptLicense:([bool] $case.AcceptLicense) `
                -Force
        }

        operation Save {
            param($case, $run)

            $run.ManagedResult = Save-ManagedModule `
                -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.RepositoryUri `
                -RepositoryName $run.RepositoryName `
                -Path $run.SaveRoot `
                -PackageCacheDirectory $run.PackageCacheRoot `
                -AcceptLicense:([bool] $case.AcceptLicense) `
                -Force
        }
    }

    engine ModuleFast {
        operation Install {
            param($case, $run)

            if (-not [string]::IsNullOrWhiteSpace([string] $run.ModuleFastModulePath)) {
                Import-Module -LiteralPath $run.ModuleFastModulePath -Force
            } elseif (-not (Get-Command -Name Install-ModuleFast -ErrorAction SilentlyContinue)) {
                Import-Module ModuleFast -ErrorAction Stop
            }

            Install-ModuleFast -Name $case.ModuleName -Path $run.InstallRoot -Source $run.ModuleFastSource -Force | Out-Null
        }
    }

    engine PSResourceGet {
        operation Find {
            param($case, $run)

            Find-PSResource -Name $case.ModuleName -Repository $run.RepositoryName | Out-Null
        }

        operation Install {
            param($case, $run)

            Install-PSResource -Name $case.ModuleName -Version $case.Version -Repository $run.RepositoryName -TrustRepository -AcceptLicense -Reinstall | Out-Null
        }

        operation Save {
            param($case, $run)

            Save-PSResource -Name $case.ModuleName -Version $case.Version -Repository $run.RepositoryName -Path $run.SaveRoot -TrustRepository -AcceptLicense | Out-Null
        }
    }

    engine PowerShellGet {
        operation Find {
            param($case, $run)

            Find-Module -Name $case.ModuleName -Repository $run.RepositoryName | Out-Null
        }

        operation Install {
            param($case, $run)

            Install-Module -Name $case.ModuleName -RequiredVersion $case.Version -Repository $run.RepositoryName -Scope CurrentUser -AllowClobber -AcceptLicense -Force | Out-Null
        }

        operation Save {
            param($case, $run)

            Save-Module -Name $case.ModuleName -RequiredVersion $case.Version -Repository $run.RepositoryName -Path $run.SaveRoot -AcceptLicense -Force | Out-Null
        }
    }

    validate {
        param($case, $run)

        if ([string] $case.Operation -in @('Install', 'Save') -and [string] $case.Engine -in @('Managed', 'ModuleFast')) {
            $root = if ([string] $case.Operation -eq 'Save') { $run.SaveRoot } else { $run.InstallRoot }
            if (-not (Test-Path -LiteralPath (Join-Path $root $case.ModuleName))) {
                throw "Expected module '$($case.ModuleName)' under '$root'."
            }
        }
    }

    metric DependencyCount {
        param($case, $run)

        if ($null -eq $run.ManagedResult -or $null -eq $run.ManagedResult.DependenciesInstalled) {
            return 0
        }

        return [double] $run.ManagedResult.DependenciesInstalled.Count
    }

    comparison Engine -Baseline Managed -Metric MedianMs
    readme (Join-Path $repositoryRoot 'README.MD') -Block 'managed-module-benchmark-table' -Renderer ComparisonTable
    artifacts Json, Csv, Markdown
}
