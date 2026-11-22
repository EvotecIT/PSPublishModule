param()

$powerForge = Get-BenchmarkInput PowerForgePath
$moduleFast = Get-BenchmarkInput ModuleFastPath
$psResourceGet = Get-BenchmarkInput PSResourceGetPath
$psRepositoryDirectory = Get-BenchmarkInput PSResourceGetRepositoryDirectory
$seedModules = Get-BenchmarkInput PSResourceGetInstalledModules
$psWorker = Get-BenchmarkInput PSResourceGetWorkerPath
$feed = Get-BenchmarkInput Source
$proofPath = Get-BenchmarkInput PackageProofPath
$workRoot = Get-BenchmarkInput WorkRoot
$outputRoot = Get-BenchmarkInput OutputRoot
$processorSet = Get-BenchmarkInput ProcessorSet 'Host default'
$baseline = Get-BenchmarkInput ComparisonBaseline $(if ($psResourceGet) { 'PowerForge' } else { 'ModuleFast' })

foreach ($path in @($powerForge, $moduleFast, $proofPath)) {
    if (-not $path -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'Supply PowerForgePath, ModuleFastPath, and PackageProofPath for the exact candidate executables and feed fixture.'
    }
}
if (-not $feed -or -not $workRoot -or -not $outputRoot) {
    throw 'Supply Source, WorkRoot, and OutputRoot for the comparison.'
}
$packageProof = Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json -AsHashtable

New-BenchmarkSuite 'managed-module-cli' -OutputRoot $outputRoot {
    Set-BenchmarkPolicy -Warmup 2 -Iterations 10 -Order Rotated -OutlierMode None
    Add-BenchmarkMetadata Source $feed
    Add-BenchmarkMetadata ProcessorSet $processorSet
    Add-BenchmarkMetadata Platform ([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)
    Add-BenchmarkMetadata PowerForgeSha256 (Get-FileHash -LiteralPath $powerForge -Algorithm SHA256).Hash
    Add-BenchmarkMetadata ModuleFastSha256 (Get-FileHash -LiteralPath $moduleFast -Algorithm SHA256).Hash
    Add-BenchmarkMetadata PowerForgeBytes (Get-Item -LiteralPath $powerForge).Length
    Add-BenchmarkMetadata ModuleFastBytes (Get-Item -LiteralPath $moduleFast).Length
    Add-BenchmarkMetadata PackageProofSha256 (Get-FileHash -LiteralPath $proofPath -Algorithm SHA256).Hash

    $cases = @(
        [pscustomobject]@{ Name = 'Startup'; Modules = @() }
        [pscustomobject]@{ Name = 'FirstBundleUse'; Modules = @() }
        [pscustomobject]@{ Name = 'Pester'; Modules = @('Pester') }
        [pscustomobject]@{ Name = 'PSScriptAnalyzer'; Modules = @('PSScriptAnalyzer') }
        [pscustomobject]@{ Name = 'GraphUsers'; Modules = @('Microsoft.Graph.Users', 'Microsoft.Graph.Authentication') }
    )

    if ($psResourceGet) {
        Add-BenchmarkMetadata PSResourceGetPath $psResourceGet
        Add-BenchmarkMetadata PSResourceGetSha256 (Get-FileHash -LiteralPath $psResourceGet -Algorithm SHA256).Hash
        $cases += @('Find', 'Save', 'List', 'Uninstall') | ForEach-Object {
            [pscustomobject]@{ Name = "Pester$_"; Modules = @('Pester'); Action = $_ }
        }
    }
    Add-BenchmarkCaseSource $cases
    Add-BenchmarkSkipRule {
        param($case)
        ($case.Action -and $case.Engine -eq 'ModuleFast') -or
            ($case.Scenario -eq 'FirstBundleUse' -and ($case.Engine -eq 'PSResourceGet' -or $baseline -eq 'PSResourceGet'))
    }

    Set-BenchmarkSetup {
        param($case, $run)

        $run.Scratch = Join-Path $workRoot ([guid]::NewGuid().ToString('N'))
        $run.IsolatedHome = Join-Path $run.Scratch 'home'
        $run.Destination = Join-Path $run.IsolatedHome '.local/share/powershell/Modules'
        $run.Data = Join-Path $run.Scratch 'data'
        $run.Log = Join-Path $run.Scratch 'output.log'
        $run.BundleRoot = if ($case.Scenario -eq 'FirstBundleUse') {
            Join-Path $run.Scratch 'bundle'
        } else {
            Join-Path $workRoot 'warm-bundle'
        }
        if ($case.Scenario -eq 'FirstBundleUse' -and (Test-Path -LiteralPath $run.BundleRoot)) {
            throw 'First bundle use requires an empty extraction directory.'
        }
        New-Item -ItemType Directory -Path $run.Scratch -Force | Out-Null
        $name = $case.Modules | Select-Object -First 1
        if ($name) {
            $run.Version = $packageProof[$name].version
        }
        $run.Action = if ($case.Action) { $case.Action } elseif ($name) { 'Install' } else { 'Startup' }
        if ($run.Action -in 'List', 'Uninstall') {
            $null = New-Item -ItemType Directory -Path $run.Destination
            Copy-Item -LiteralPath (Join-Path $seedModules 'Pester') -Destination $run.Destination -Recurse
        }
        if ($case.Engine -eq 'PowerForge') {
            $run.Executable = $powerForge
            $run.Arguments = switch ($run.Action) {
                { $_ -in 'Install', 'Save' } {
                    @('module', $_.ToLowerInvariant(), $name, '--version', $run.Version, '--source', $feed,
                        '--destination', $run.Destination, '--accept-license', '--allow-clobber', '--output', 'json')
                }
                'Find' { @('module', 'find', '--name', $name, '--version', $run.Version, '--source', $feed, '--output', 'json') }
                'List' { @('module', 'list', '--name', $name, '--version', $run.Version, '--destination', $run.Destination, '--output', 'json') }
                'Uninstall' { @('module', 'uninstall', $name, '--version', $run.Version, '--destination', $run.Destination, '--output', 'json') }
                default { @('module', 'install', '--help') }
            }
            $run.HelpPattern = 'powerforge module install'
        } elseif ($case.Engine -eq 'PSResourceGet') {
            $null = New-Item -ItemType Directory -Path $run.IsolatedHome, $run.Data -Force
            Copy-Item -LiteralPath $psRepositoryDirectory -Destination $run.Data -Recurse
            if ($run.Action -eq 'Save') { $null = New-Item -ItemType Directory -Path $run.Destination }
            $run.Executable = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
            $run.Arguments = @('-NoProfile', '-NonInteractive', '-File', $psWorker, '-Operation', $run.Action,
                '-ModulePath', $psResourceGet, '-IsolatedHome', $run.IsolatedHome, '-Destination', $run.Destination, '-Source', $feed)
            if ($name) { $run.Arguments += @('-Name', $name, '-Version', $run.Version) }
            $run.HelpPattern = 'Install-PSResource'
        } else {
            $run.Executable = $moduleFast
            $run.Arguments = if ($name) {
                @('--spec', "$name=$($run.Version)", '--source', $feed, '--destination', $run.Destination, '--destinationonly')
            } else {
                @('--help')
            }
            $run.HelpPattern = 'Fast PowerShell module installer'
        }
    }

    $launch = {
        param($case, $run)

        [string[]] $arguments = @($run.Arguments)
        try {
            if ($case.Engine -eq 'PSResourceGet') {
                $environment = @('-u', 'PSModulePath', "HOME=$($run.IsolatedHome)", "XDG_DATA_HOME=$($run.Data)",
                    "XDG_CACHE_HOME=$($run.Scratch)/cache", "XDG_CONFIG_HOME=$($run.Scratch)/config")
                & /usr/bin/env @environment $run.Executable @arguments > $run.Log 2>&1
            } else {
                & /usr/bin/env -u PSModulePath 'PATH=/nonexistent' "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$($run.BundleRoot)" $run.Executable @arguments > $run.Log 2>&1
            }
            $run.ExitCode = $LASTEXITCODE
        } catch {
            $diagnostics = Get-Content -LiteralPath $run.Log -Raw -ErrorAction SilentlyContinue
            throw "$($case.Engine) failed: $diagnostics `n$_"
        }
    }

    Add-BenchmarkEngine PowerForge {
        Add-BenchmarkOperation Invoke $launch
    }
    Add-BenchmarkEngine ModuleFast {
        Add-BenchmarkOperation Invoke $launch
    }

    if ($psResourceGet) {
        Add-BenchmarkEngine PSResourceGet {
            Add-BenchmarkOperation Invoke $launch
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)

        try {
            if ($run.ExitCode -ne 0) {
                throw "$($case.Engine) failed with exit code $($run.ExitCode): $(Get-Content -LiteralPath $run.Log -Raw)"
            }
            if ($case.Scenario -eq 'FirstBundleUse' -and $case.Engine -eq 'PowerForge') {
                Assert-BenchmarkPath $run.BundleRoot
            }
            if ($case.Modules.Count -eq 0) {
                if ((Get-Content -LiteralPath $run.Log -Raw) -notmatch [regex]::Escape($run.HelpPattern)) {
                    throw "$($case.Engine) did not print install usage."
                }
                return
            }

            if ($run.Action -eq 'Uninstall') {
                if (Test-Path -LiteralPath (Join-Path $run.Destination 'Pester' $run.Version)) {
                    throw 'Uninstall left the exact requested version installed.'
                }
                return
            }
            if ($run.Action -in 'Find', 'List') {
                $json = Get-Content -LiteralPath $run.Log -Raw | ConvertFrom-Json
                $rows = if ($case.Engine -eq 'PowerForge') { @($json.result) } else { @($json) }
                Assert-BenchmarkValue -Actual @($rows).Count -Expected 1 -Message 'Expected exactly one module record.'
                Assert-BenchmarkValue -Actual $rows[0].Name -Expected 'Pester'
                Assert-BenchmarkValue -Actual $rows[0].Version -Expected $run.Version
                return
            }

            $installed = @(Get-ChildItem -LiteralPath $run.Destination -Directory | Where-Object Name -NotLike '.*')
            Assert-BenchmarkValue -Actual $installed.Count -Expected $case.Modules.Count -Message 'Expected the full module dependency set.'
            foreach ($name in $case.Modules) {
                $expected = $packageProof[$name]
                $moduleDirectory = $installed | Where-Object Name -EQ $name
                $manifest = Join-Path $moduleDirectory.FullName $expected.version $expected.manifest
                Assert-BenchmarkPath $manifest
                Assert-BenchmarkValue -Actual (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant() `
                    -Expected $expected.manifestSha256 -Message "Installed manifest bytes differ from the source package for $name."
            }
        } finally {
            Remove-Item -LiteralPath $run.Scratch -Recurse -Force
        }
    }

    Add-BenchmarkComparison Engine -Baseline $baseline -Metric MedianMs
    Set-BenchmarkArtifacts Json, Csv, Markdown
}
