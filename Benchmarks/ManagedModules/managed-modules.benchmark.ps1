$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$repositoryName = input RepositoryName PSGallery
$repositoryUri = input RepositoryUri 'https://www.powershellgallery.com/api/v3/index.json'
$moduleFastSource = input ModuleFastSource 'https://pwsh.gallery/index.json'
$moduleFastModulePath = input ModuleFastPath
$managedModulePath = input ManagedModulePath
$updateReadme = inputBool UpdateReadme $false
$comparisonMode = if ($repositoryUri.TrimEnd('/') -eq $moduleFastSource.TrimEnd('/')) { 'IdenticalFeed' } else { 'DefaultSources' }

$managedArtifactPath = if ($managedModulePath -and $managedModulePath.Trim()) {
    [System.IO.Path]::GetFullPath($managedModulePath)
} else {
    [System.IO.Path]::GetFullPath([string] (Get-Command Install-ManagedModule -ErrorAction Stop).DLL)
}
$managedArtifactVersion = $null
$managedArtifactSha256 = $null
if (Test-Path -LiteralPath $managedArtifactPath -PathType Leaf) {
    $managedArtifactVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($managedArtifactPath).ProductVersion
    $managedArtifactSha256 = (Get-FileHash -LiteralPath $managedArtifactPath -Algorithm SHA256).Hash
}

$moduleFastManifestPath = if ($moduleFastModulePath -and $moduleFastModulePath.Trim()) {
    [System.IO.Path]::GetFullPath($moduleFastModulePath)
} else {
    [string] (Get-Module -ListAvailable -Name ModuleFast | Sort-Object Version -Descending | Select-Object -First 1).Path
}
$moduleFastVersion = $null
$moduleFastSha256 = $null
if ($moduleFastManifestPath -and (Test-Path -LiteralPath $moduleFastManifestPath -PathType Leaf)) {
    $moduleFastManifest = Import-PowerShellDataFile -LiteralPath $moduleFastManifestPath
    $moduleFastVersion = [string] $moduleFastManifest.ModuleVersion
    $moduleFastPrerelease = $moduleFastManifest.PrivateData.PSData['Prerelease']
    if ($moduleFastPrerelease) {
        $moduleFastVersion += '-' + [string] $moduleFastPrerelease
    }
    $moduleFastBinaryPath = Join-Path (Split-Path -Parent $moduleFastManifestPath) 'ModuleFast.dll'
    if (Test-Path -LiteralPath $moduleFastBinaryPath -PathType Leaf) {
        $moduleFastSha256 = (Get-FileHash -LiteralPath $moduleFastBinaryPath -Algorithm SHA256).Hash
    }
}

benchmark 'managed-modules' -out (Join-Path $repositoryRoot 'Ignore\Benchmarks\ManagedModules') {
    metadata ComparisonMode $comparisonMode
    metadata ManagedRepositoryUri $repositoryUri
    metadata ModuleFastSource $moduleFastSource
    if ($managedArtifactVersion) { metadata ManagedModuleVersion $managedArtifactVersion }
    if ($managedArtifactSha256) { metadata ManagedModuleSha256 $managedArtifactSha256 }
    if ($moduleFastVersion) { metadata ModuleFastVersion $moduleFastVersion }
    if ($moduleFastSha256) { metadata ModuleFastSha256 $moduleFastSha256 }

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
        $run.ManagedModulePath = $managedModulePath
        $workKey = "$repositoryRoot|$PID|$($run.RunId)|$($run.Iteration)|$($case.ModuleName)|$($case.Engine)|$($case.Operation)|$($case.Host)"
        $hashBytes = [System.Security.Cryptography.SHA1]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($workKey))
        $hash = [System.BitConverter]::ToString($hashBytes).Replace('-', '').Substring(0, 12).ToLowerInvariant()
        $run.WorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) "pf-mm-$hash"
        $run.InstallRoot = Join-Path $run.WorkRoot 'installed'
        $run.SaveRoot = Join-Path $run.WorkRoot 'saved'
        $run.PackageCacheRoot = Join-Path $run.WorkRoot 'package-cache'

        if (Test-Path -LiteralPath $run.WorkRoot) {
            Remove-Item -LiteralPath $run.WorkRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Path $run.InstallRoot, $run.SaveRoot, $run.PackageCacheRoot -Force | Out-Null

        if ($case.Engine -eq 'Managed' -and $run.ManagedModulePath -and $run.ManagedModulePath.Trim()) {
            $requestedManagedPath = [System.IO.Path]::GetFullPath($run.ManagedModulePath)
            $run.ManagedExpectedSha256 = (Get-FileHash -LiteralPath $requestedManagedPath -Algorithm SHA256).Hash
            Import-Module -Name $requestedManagedPath -Force -ErrorAction Stop
            $run.ManagedCommandPath = [System.IO.Path]::GetFullPath([string] (Get-Command Install-ManagedModule -ErrorAction Stop).DLL)
            $run.ManagedCommandSha256 = (Get-FileHash -LiteralPath $run.ManagedCommandPath -Algorithm SHA256).Hash
        } elseif ($case.Engine -eq 'ModuleFast') {
            Remove-Module ModuleFast -Force -ErrorAction SilentlyContinue
            if ($run.ModuleFastModulePath -and $run.ModuleFastModulePath.Trim()) {
                Import-Module -Name $run.ModuleFastModulePath -Force -ErrorAction Stop
            } else {
                Import-Module ModuleFast -Force -ErrorAction Stop
            }
            if ($run.ModuleFastModulePath -and $run.ModuleFastModulePath.Trim()) {
                $expectedModuleFastBinary = Join-Path (Split-Path -Parent ([System.IO.Path]::GetFullPath($run.ModuleFastModulePath))) 'ModuleFast.dll'
                $loadedModuleFastBinary = Join-Path (Get-Module ModuleFast -ErrorAction Stop).ModuleBase 'ModuleFast.dll'
                $run.ModuleFastExpectedSha256 = (Get-FileHash -LiteralPath $expectedModuleFastBinary -Algorithm SHA256).Hash
                $run.ModuleFastCommandSha256 = (Get-FileHash -LiteralPath $loadedModuleFastBinary -Algorithm SHA256).Hash
            }
            Clear-ModuleFastCache
        }
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

            Install-ModuleFast "$($case.ModuleName)=$($case.Version)" `
                -Destination $run.InstallRoot `
                -Source $run.ModuleFastSource `
                -DestinationOnly `
                -NoPSModulePathUpdate `
                -Confirm:$false | Out-Null
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

        $moduleRoot = Join-Path $root $case.ModuleName
        assertPath $moduleRoot
        $manifests = @(Get-ChildItem -LiteralPath $moduleRoot -Recurse -File -Filter "$($case.ModuleName).psd1")
        assertValue -Actual $manifests.Count -Expected 1 -Message 'Expected exactly one requested module manifest.'
        $manifest = Import-PowerShellDataFile -Path $manifests[0].FullName
        assertValue -Actual ([string] $manifest.ModuleVersion) -Expected $case.Version -Message 'Installed manifest version must match the requested exact version.'
        if ($case.Engine -eq 'Managed' -and $run.ManagedModulePath -and $run.ManagedModulePath.Trim()) {
            assertValue -Actual $run.ManagedCommandSha256 -Expected $run.ManagedExpectedSha256 -Message 'Managed benchmark must use the pinned PSPublishModule binary bytes.'
        }
        if ($case.Engine -eq 'ModuleFast' -and $run.ModuleFastModulePath -and $run.ModuleFastModulePath.Trim()) {
            assertValue -Actual $run.ModuleFastCommandSha256 -Expected $run.ModuleFastExpectedSha256 -Message 'ModuleFast benchmark must use the pinned ModuleFast binary bytes.'
        }
    }

    metric DependencyCount {
        param($case, $run)

        if ($null -eq $run.ManagedResult -or $null -eq $run.ManagedResult.DependenciesInstalled) {
            return 0
        }

        return $run.ManagedResult.DependenciesInstalled.Count
    }

    metric InstalledFileCount {
        param($case, $run)

        $root = if ($case.Operation -eq 'Save') { $run.SaveRoot } else { $run.InstallRoot }
        return @(Get-ChildItem -LiteralPath $root -Recurse -File).Count
    }

    metric InstalledBytes {
        param($case, $run)

        $root = if ($case.Operation -eq 'Save') { $run.SaveRoot } else { $run.InstallRoot }
        return [long] ((Get-ChildItem -LiteralPath $root -Recurse -File | Measure-Object -Property Length -Sum).Sum)
    }

    comparison Engine -Baseline Managed -Metric MedianMs
    if ($updateReadme) {
        readme (Join-Path $repositoryRoot 'README.MD') -Block 'managed-module-benchmark-table' -Renderer ComparisonTable
    }
    artifacts Json, Csv, Markdown
}
