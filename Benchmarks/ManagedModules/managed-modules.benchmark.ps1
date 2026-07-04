$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$repositoryName = input RepositoryName PSGallery
$repositoryUri = input RepositoryUri 'https://www.powershellgallery.com/api/v3/index.json'
$moduleFastSource = input ModuleFastSource 'https://pwsh.gallery/index.json'
$moduleFastModulePath = input ModuleFastPath

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
    axis Host Core, Desktop

    setup {
        param($case, $run)

        $run.RepositoryName = $repositoryName
        $run.RepositoryUri = $repositoryUri
        $run.ModuleFastSource = $moduleFastSource
        $run.ModuleFastModulePath = $moduleFastModulePath
        $hashBytes = [System.Security.Cryptography.SHA1]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($run.OutputDirectory))
        $hash = [System.BitConverter]::ToString($hashBytes).Replace('-', '').Substring(0, 12).ToLowerInvariant()
        $run.WorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) "pf-mm-$hash"
        $run.InstallRoot = Join-Path $run.WorkRoot 'installed'
        $run.SaveRoot = Join-Path $run.WorkRoot 'saved'
        $run.PackageCacheRoot = Join-Path $run.WorkRoot 'package-cache'

        if (Test-Path -LiteralPath $run.WorkRoot) {
            Remove-Item -LiteralPath $run.WorkRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Path $run.InstallRoot, $run.SaveRoot, $run.PackageCacheRoot -Force | Out-Null
    }

    skip {
        param($case)

        if ($case.Engine -eq 'ModuleFast' -and $case.Operation -ne 'Install') {
            return $true
        }

        if ($case.Engine -eq 'ModuleFast' -and $case.Host -notlike 'Core*') {
            return $true
        }

        if ($case.Engine -eq 'PSResourceGet' -and $case.Host -notlike 'Core*') {
            return $true
        }

        if ($case.Engine -eq 'PSResourceGet' -and -not (Get-Module -ListAvailable -Name Microsoft.PowerShell.PSResourceGet)) {
            return $true
        }

        if ($case.Engine -eq 'PowerShellGet' -and -not (Get-Module -ListAvailable -Name PowerShellGet)) {
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
                -AllowClobber `
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

    engine PSResourceGet {
        operation Find {
            param($case, $run)

            Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop
            Find-PSResource -Name $case.ModuleName -Repository $run.RepositoryName | Out-Null
        }

        operation Install {
            param($case, $run)

            Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop
            Install-PSResource -Name $case.ModuleName -Version $case.Version -Repository $run.RepositoryName -TrustRepository -AcceptLicense -Reinstall | Out-Null
        }

        operation Save {
            param($case, $run)

            Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop
            Save-PSResource -Name $case.ModuleName -Version $case.Version -Repository $run.RepositoryName -Path $run.SaveRoot -TrustRepository -AcceptLicense | Out-Null
        }
    }

    engine PowerShellGet {
        operation Find {
            param($case, $run)

            Import-Module PowerShellGet -ErrorAction Stop
            Find-Module -Name $case.ModuleName -Repository $run.RepositoryName | Out-Null
        }

        operation Install {
            param($case, $run)

            Import-Module PowerShellGet -ErrorAction Stop
            Install-Module -Name $case.ModuleName -RequiredVersion $case.Version -Repository $run.RepositoryName -Scope CurrentUser -AllowClobber -AcceptLicense -Force | Out-Null
        }

        operation Save {
            param($case, $run)

            Import-Module PowerShellGet -ErrorAction Stop
            Save-Module -Name $case.ModuleName -RequiredVersion $case.Version -Repository $run.RepositoryName -Path $run.SaveRoot -AcceptLicense -Force | Out-Null
        }
    }

    validate {
        param($case, $run)

        $root = switch ($case.Operation) {
            'Install' {
                if ($case.Engine -notin @('Managed', 'ModuleFast')) { return }
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
