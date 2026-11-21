param()

$powerForge = Get-BenchmarkInput PowerForgePath
$moduleFast = Get-BenchmarkInput ModuleFastPath
$feed = Get-BenchmarkInput Source
$proofPath = Get-BenchmarkInput PackageProofPath
$workRoot = Get-BenchmarkInput WorkRoot
$outputRoot = Get-BenchmarkInput OutputRoot
$processorSet = Get-BenchmarkInput ProcessorSet 'Host default'

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

    Add-BenchmarkCaseSource @(
        [pscustomobject]@{ Name = 'Startup'; Modules = @() }
        [pscustomobject]@{ Name = 'FirstBundleUse'; Modules = @() }
        [pscustomobject]@{ Name = 'Pester'; Modules = @('Pester') }
        [pscustomobject]@{ Name = 'PSScriptAnalyzer'; Modules = @('PSScriptAnalyzer') }
        [pscustomobject]@{ Name = 'GraphUsers'; Modules = @('Microsoft.Graph.Users', 'Microsoft.Graph.Authentication') }
    )

    Set-BenchmarkSetup {
        param($case, $run)

        $run.Scratch = Join-Path $workRoot ([guid]::NewGuid().ToString('N'))
        $run.Destination = Join-Path $run.Scratch 'Modules'
        $run.Log = Join-Path $run.Scratch 'output.log'
        $run.BundleRoot = if ($case.Name -eq 'FirstBundleUse') {
            Join-Path $run.Scratch 'bundle'
        } else {
            Join-Path $workRoot 'warm-bundle'
        }
        New-Item -ItemType Directory -Path $run.Scratch -Force | Out-Null
        $name = $case.Modules | Select-Object -First 1
        if ($name) {
            $run.Version = $packageProof[$name].version
        }
        if ($case.Engine -eq 'PowerForge') {
            $run.Executable = $powerForge
            $run.Arguments = if ($name) {
                @('module', 'install', $name, '--version', $run.Version, '--source', $feed, '--destination', $run.Destination, '--accept-license', '--allow-clobber')
            } else {
                @('module', 'install', '--help')
            }
            $run.HelpPattern = 'powerforge module install'
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
        & /usr/bin/env 'PATH=/nonexistent' "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$($run.BundleRoot)" $run.Executable @arguments > $run.Log 2>&1
        $run.ExitCode = $LASTEXITCODE
    }

    Add-BenchmarkEngine PowerForge {
        Add-BenchmarkOperation Invoke $launch
    }
    Add-BenchmarkEngine ModuleFast {
        Add-BenchmarkOperation Invoke $launch
    }

    Add-BenchmarkValidation {
        param($case, $run)

        try {
            if ($run.ExitCode -ne 0) {
                throw "$($case.Engine) failed with exit code $($run.ExitCode): $(Get-Content -LiteralPath $run.Log -Raw)"
            }
            if ($case.Modules.Count -eq 0) {
                if ((Get-Content -LiteralPath $run.Log -Raw) -notmatch [regex]::Escape($run.HelpPattern)) {
                    throw "$($case.Engine) did not print install usage."
                }
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

    Add-BenchmarkComparison Engine -Baseline ModuleFast -Metric MedianMs
    Set-BenchmarkArtifacts Json, Csv, Markdown
}
