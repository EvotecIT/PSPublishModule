$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

benchmark 'managed-modules' -out (Join-Path $repositoryRoot 'Ignore\Benchmarks\ManagedModules') {
    policy -Warmup 1 -Iterations 3 -Order Rotated -OutlierMode None
    profile Current -Cleanup KeepOnFailure
    caseSource @(
        [pscustomobject]@{ Name = 'SingleModule'; ModuleName = 'PSScriptAnalyzer'; Version = '1.25.0'; AcceptLicense = $false }
        [pscustomobject]@{ Name = 'GraphAuthentication'; ModuleName = 'Microsoft.Graph.Authentication'; Version = '2.29.1'; AcceptLicense = $true }
        [pscustomobject]@{ Name = 'Graph'; ModuleName = 'Microsoft.Graph'; Version = '2.29.1'; AcceptLicense = $true }
        [pscustomobject]@{ Name = 'AzAccounts'; ModuleName = 'Az.Accounts'; Version = '5.1.0'; AcceptLicense = $true }
        [pscustomobject]@{ Name = 'Az'; ModuleName = 'Az'; Version = '14.0.0'; AcceptLicense = $true }
    )
    axis Host Current

    setup {
        param($case, $run)

        $run.RepositoryName = $BenchmarkVariables['RepositoryName'] ?? 'PSGallery'
        $run.RepositoryUri = $BenchmarkVariables['RepositoryUri'] ?? 'https://www.powershellgallery.com/api/v3/index.json'
        $run.ModuleFastSource = $BenchmarkVariables['ModuleFastSource'] ?? 'https://pwsh.gallery/index.json'
        $run.ModuleFastModulePath = $BenchmarkVariables['ModuleFastPath'] ?? ''
        $run.ModuleFastCSharpPath = $BenchmarkVariables['ModuleFastCSharpPath'] ?? ''
        $run.InstallRoot = Join-Path $run.OutputDirectory 'installed'
        $run.SaveRoot = Join-Path $run.OutputDirectory 'saved'
        $run.PackageCacheRoot = Join-Path $run.OutputDirectory 'package-cache'

        New-Item -ItemType Directory -Path $run.InstallRoot, $run.SaveRoot, $run.PackageCacheRoot -Force | Out-Null
    }

    skip {
        param($case)

        if ($case.Engine -in @('ModuleFast', 'ModuleFastCSharp') -and $case.Operation -ne 'Install') {
            return $true
        }

        if ($case.Engine -eq 'ModuleFastCSharp' -and -not ($BenchmarkVariables['ModuleFastCSharpPath'] ?? '').Trim()) {
            return $true
        }

        if ($case.Profile -ne 'TemporaryLocalUser' -and
            $case.Operation -eq 'Install' -and
            @('PSResourceGet', 'PowerShellGet') -contains $case.Engine) {
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
                -AcceptLicense:$case.AcceptLicense `
                -AllowClobber `
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
                -AcceptLicense:$case.AcceptLicense `
                -Force
        }
    }

    engine ModuleFast {
        operation Install {
            param($case, $run)

            Remove-Module ModuleFast -Force -ErrorAction SilentlyContinue
            if ($run.ModuleFastModulePath -and $run.ModuleFastModulePath.Trim()) {
                Import-Module -Name $run.ModuleFastModulePath -Force
            } else {
                Import-Module ModuleFast -ErrorAction Stop
            }

            Install-ModuleFast "$($case.ModuleName)=$($case.Version)" `
                -Destination $run.InstallRoot `
                -Source $run.ModuleFastSource `
                -NoPSModulePathUpdate | Out-Null
        }
    }

    engine ModuleFastCSharp {
        operation Install {
            param($case, $run)

            Remove-Module ModuleFast -Force -ErrorAction SilentlyContinue
            Import-Module -Name $run.ModuleFastCSharpPath -Force

            Install-ModuleFast "$($case.ModuleName)=$($case.Version)" `
                -Destination $run.InstallRoot `
                -Source $run.ModuleFastSource `
                -NoPSModulePathUpdate | Out-Null
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

        $root = switch ($case.Operation) {
            'Install' {
                if ($case.Engine -notin @('Managed', 'ModuleFast', 'ModuleFastCSharp')) { return }
                $run.InstallRoot
            }
            'Save' { $run.SaveRoot }
            default { return }
        }

        assertPath (Join-Path $root $case.ModuleName)
    }

    metric DependencyCount {
        param($case, $run)

        if ($null -eq $run.ManagedResult -or $null -eq $run.ManagedResult.DependenciesInstalled) {
            return 0
        }

        return $run.ManagedResult.DependenciesInstalled.Count
    }

    comparison Engine -Baseline Managed -Metric MedianMs
    readme (Join-Path $repositoryRoot 'README.MD') -Block 'managed-module-benchmark-table' -Renderer ComparisonTable
    artifacts Json, Csv, Markdown
}
