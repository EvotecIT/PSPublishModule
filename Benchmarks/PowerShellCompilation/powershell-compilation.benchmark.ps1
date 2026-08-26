$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$outputRoot = Join-Path $repositoryRoot 'Ignore\Benchmarks\PowerShellCompilation'
$buildRoot = Join-Path $outputRoot 'artifacts'
$workloadPath = Join-Path $PSScriptRoot 'compiler-workloads.ps1'
$startupScriptPath = Join-Path $PSScriptRoot 'packaged-startup.ps1'
$optimizedExecutableScriptPath = Join-Path $PSScriptRoot 'typed-executable-optimization.ps1'
$localCallEntryPath = Join-Path $PSScriptRoot 'typed-local-call-main.ps1'
$localCallHelperPath = Join-Path $PSScriptRoot 'typed-local-call-helper.ps1'
$harnessPath = Join-Path $PSScriptRoot 'PowerShellCompilationBenchmarkHarness.cs'
$quick = Get-BenchmarkInput Quick $false -Bool
$calls = [int](Get-BenchmarkInput Calls $(if ($quick) { 2000 } else { 50000 }))
$loopCalls = [int](Get-BenchmarkInput LoopCalls $(if ($quick) { 100 } else { 1000 }))
$warmup = [int](Get-BenchmarkInput Warmup $(if ($quick) { 1 } else { 3 }))
$iterations = [int](Get-BenchmarkInput Iterations $(if ($quick) { 3 } else { 12 }))
$includeOptimizedExecutables = Get-BenchmarkInput IncludeOptimizedExecutables $false -Bool
$targetFramework = if ([System.Environment]::Version.Major -ge 10) { 'net10.0' } else { 'net8.0' }
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$defaultRid = if ($IsWindows) { "win-$architecture" } elseif ($IsMacOS) { "osx-$architecture" } else { "linux-$architecture" }
$runtimeIdentifier = Get-BenchmarkInput RuntimeIdentifier $defaultRid

New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$builder = [PowerForge.PowerShellCompilationArtifactBuilder]::new()

$typedSpec = [PowerForge.PowerShellCompilationBuildSpec]::new(
    $workloadPath,
    $buildRoot,
    'PowerForge.CompilationBenchmark',
    [PowerForge.PowerShellCompilationArtifactKind]::Library,
    [PowerForge.PowerShellCompilationMode]::Strict)
$typedSpec.TargetFramework = $targetFramework
$typedResult = $builder.Build($typedSpec)
if (-not $typedResult.Succeeded) {
    throw "Typed benchmark library failed: $($typedResult.Error)`n$($typedResult.BuildOutput)"
}

$moduleSpec = [PowerForge.PowerShellCompilationBuildSpec]::new(
    $workloadPath,
    $buildRoot,
    'PowerForge.CompilationBenchmark.Module',
    [PowerForge.PowerShellCompilationArtifactKind]::BinaryModule,
    [PowerForge.PowerShellCompilationMode]::Strict)
$moduleSpec.TargetFramework = $targetFramework
$moduleResult = $builder.Build($moduleSpec)
if (-not $moduleResult.Succeeded) {
    throw "Binary benchmark module failed: $($moduleResult.Error)`n$($moduleResult.BuildOutput)"
}

$executableSpec = [PowerForge.PowerShellCompilationBuildSpec]::new(
    $startupScriptPath,
    $buildRoot,
    'PowerForge.CompilationBenchmark.Startup',
    [PowerForge.PowerShellCompilationArtifactKind]::Executable,
    [PowerForge.PowerShellCompilationMode]::Package)
$executableSpec.TargetFramework = $targetFramework
$executableResult = $builder.Build($executableSpec)
if (-not $executableResult.Succeeded) {
    throw "Packaged benchmark executable failed: $($executableResult.Error)`n$($executableResult.BuildOutput)"
}

$typedExecutableSpec = [PowerForge.PowerShellCompilationBuildSpec]::new(
    $startupScriptPath,
    $buildRoot,
    'PowerForge.CompilationBenchmark.TypedStartup',
    [PowerForge.PowerShellCompilationArtifactKind]::Executable,
    [PowerForge.PowerShellCompilationMode]::Strict)
$typedExecutableSpec.TargetFramework = $targetFramework
$typedExecutableResult = $builder.Build($typedExecutableSpec)
if (-not $typedExecutableResult.Succeeded) {
    throw "Typed benchmark executable failed: $($typedExecutableResult.Error)`n$($typedExecutableResult.BuildOutput)"
}

$localCallSpec = [PowerForge.PowerShellCompilationBuildSpec]::new(
    $localCallEntryPath,
    $buildRoot,
    'PowerForge.CompilationBenchmark.TypedLocalCalls',
    [PowerForge.PowerShellCompilationArtifactKind]::Executable,
    [PowerForge.PowerShellCompilationMode]::Strict)
$localCallSpec.TargetFramework = $targetFramework
$localCallSpec.CompilationSourcePaths = @($localCallEntryPath, $localCallHelperPath)
$localCallResult = $builder.Build($localCallSpec)
if (-not $localCallResult.Succeeded) {
    throw "Typed local-call benchmark executable failed: $($localCallResult.Error)`n$($localCallResult.BuildOutput)"
}

$optimizedExecutableEvidence = [ordered]@{}
if ($includeOptimizedExecutables) {
    foreach ($optimization in @(
        [PowerForge.PowerShellCompilationExecutableOptimization]::Trimmed,
        [PowerForge.PowerShellCompilationExecutableOptimization]::NativeAot)) {
        $optimizationName = $optimization.ToString()
        $optimizationSpec = [PowerForge.PowerShellCompilationBuildSpec]::new(
            $optimizedExecutableScriptPath,
            $buildRoot,
            "PowerForge.CompilationBenchmark.Typed$optimizationName",
            [PowerForge.PowerShellCompilationArtifactKind]::Executable,
            [PowerForge.PowerShellCompilationMode]::Strict)
        $optimizationSpec.TargetFramework = $targetFramework
        $optimizationSpec.RuntimeIdentifier = $runtimeIdentifier
        $optimizationSpec.SelfContained = $true
        $optimizationSpec.SingleFile = $true
        $optimizationSpec.Optimization = $optimization
        $optimizationResult = $builder.Build($optimizationSpec)
        if (-not $optimizationResult.Succeeded) {
            throw "$optimizationName typed executable failed: $($optimizationResult.Error)`n$($optimizationResult.BuildOutput)"
        }
        $verificationOutput = @(& $optimizationResult.ArtifactPath --Count=5 --Values=10 --Values=-3)
        if ($LASTEXITCODE -ne 0 -or $verificationOutput.Count -ne 1 -or [long]$verificationOutput[0] -ne 22L) {
            throw "$optimizationName typed executable returned an invalid verification result."
        }
        $optimizedExecutableEvidence[$optimizationName] = [pscustomobject]@{
            Sha256 = (Get-FileHash -LiteralPath $optimizationResult.ArtifactPath -Algorithm SHA256).Hash
            Bytes = (Get-Item -LiteralPath $optimizationResult.ArtifactPath).Length
        }
    }
}

Add-Type -Path $typedResult.ArtifactPath
Add-Type -TypeDefinition (Get-Content -LiteralPath $harnessPath -Raw) -ReferencedAssemblies $typedResult.ArtifactPath -CompilerOptions '/optimize' -IgnoreWarnings
$typedHash = (Get-FileHash -LiteralPath $typedResult.ArtifactPath -Algorithm SHA256).Hash
$moduleHash = (Get-FileHash -LiteralPath $moduleResult.ArtifactPath -Algorithm SHA256).Hash
$executableHash = (Get-FileHash -LiteralPath $executableResult.ArtifactPath -Algorithm SHA256).Hash
$typedExecutableHash = (Get-FileHash -LiteralPath $typedExecutableResult.ArtifactPath -Algorithm SHA256).Hash
$localCallExecutableHash = (Get-FileHash -LiteralPath $localCallResult.ArtifactPath -Algorithm SHA256).Hash
$currentPowerShell = (Get-Process -Id $PID).Path
$moduleQualifier = [System.IO.Path]::GetFileNameWithoutExtension($moduleResult.ArtifactPath)

New-BenchmarkSuite 'powershell-compilation-real-function' -OutputRoot $outputRoot {
    Add-BenchmarkMetadata Workload 'Production threshold calculation'
    Add-BenchmarkMetadata TypedArtifactSha256 $typedHash
    Add-BenchmarkMetadata BinaryModuleSha256 $moduleHash
    Set-BenchmarkPolicy -Warmup $warmup -Iterations $iterations -Order GroupedRotated -OutlierMode ExcludeMinMax
    Add-BenchmarkCaseSource @(
        [pscustomobject]@{ Name = 'AbsoluteCap'; Calls = $calls; BaselineMs = 100.0; RelativeTolerance = 0.2; AbsoluteToleranceMs = 30.0; Expected = 130.0 }
        [pscustomobject]@{ Name = 'RelativeCap'; Calls = $calls; BaselineMs = 100.0; RelativeTolerance = 0.5; AbsoluteToleranceMs = 30.0; Expected = 150.0 }
    )

    Set-BenchmarkSetup {
        param($case, $run)
        . $workloadPath
        Set-Item -Path Function:\global:Get-AllowedAverageMs -Value ${function:Get-AllowedAverageMs}
        Import-Module -Name $moduleResult.ArtifactPath -Global -Force -ErrorAction Stop
    }

    Add-BenchmarkEngine PowerShellFunction {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            [double] $result = 0
            for ([int] $index = 0; $index -lt $case.Calls; $index++) {
                $result = Get-AllowedAverageMs $case.BaselineMs $case.RelativeTolerance $case.AbsoluteToleranceMs
            }
            $run.Result = $result
        }
    }

    Add-BenchmarkEngine BinaryCmdlet {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            [double] $result = 0
            for ([int] $index = 0; $index -lt $case.Calls; $index++) {
                $result = & "$moduleQualifier\Get-AllowedAverageMs" $case.BaselineMs $case.RelativeTolerance $case.AbsoluteToleranceMs
            }
            $run.Result = $result
        }
    }

    Add-BenchmarkEngine TypedClr {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunTypedBranch(
                $case.Calls,
                $case.BaselineMs,
                $case.RelativeTolerance,
                $case.AbsoluteToleranceMs)
        }
    }

    Add-BenchmarkEngine HandWrittenCSharp {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunHandWrittenBranch(
                $case.Calls,
                $case.BaselineMs,
                $case.RelativeTolerance,
                $case.AbsoluteToleranceMs)
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)
        Assert-BenchmarkValue -Actual ([double] $run.Result) -Expected ([double] $case.Expected)
    }
    Add-BenchmarkComparison -Dimension Engine -Baseline HandWrittenCSharp -Metric MedianMs -TieTolerance 0.05
}

New-BenchmarkSuite 'powershell-compilation-powerinfoblox-ptr' -OutputRoot $outputRoot {
    Add-BenchmarkMetadata Workload 'PowerInfoBlox IPv4-to-PTR helper'
    Add-BenchmarkMetadata TypedArtifactSha256 $typedHash
    Add-BenchmarkMetadata BinaryModuleSha256 $moduleHash
    Set-BenchmarkPolicy -Warmup $warmup -Iterations $iterations -Order GroupedRotated -OutlierMode ExcludeMinMax
    Add-BenchmarkCase Convert @{ Calls = $calls; Address = '192.168.100.20'; Expected = '20.100.168.192.in-addr.arpa' }

    Set-BenchmarkSetup {
        param($case, $run)
        . $workloadPath
        Set-Item -Path Function:\global:Convert-IpAddressToPtrString -Value ${function:Convert-IpAddressToPtrString}
        Import-Module -Name $moduleResult.ArtifactPath -Global -Force -ErrorAction Stop
    }

    Add-BenchmarkEngine PowerShellFunction {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $result = ''
            for ([int] $index = 0; $index -lt $case.Calls; $index++) {
                $result = Convert-IpAddressToPtrString $case.Address
            }
            $run.Result = $result
        }
    }

    Add-BenchmarkEngine BinaryCmdlet {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $result = ''
            for ([int] $index = 0; $index -lt $case.Calls; $index++) {
                $result = & "$moduleQualifier\Convert-IpAddressToPtrString" $case.Address
            }
            $run.Result = $result
        }
    }

    Add-BenchmarkEngine TypedClr {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunTypedPtrConversion(
                $case.Calls,
                $case.Address)
        }
    }

    Add-BenchmarkEngine HandWrittenCSharp {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunHandWrittenPtrConversion(
                $case.Calls,
                $case.Address)
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)
        Assert-BenchmarkValue -Actual ([string] $run.Result) -Expected ([string] $case.Expected)
    }
    Add-BenchmarkComparison -Dimension Engine -Baseline HandWrittenCSharp -Metric MedianMs -TieTolerance 0.05
}

New-BenchmarkSuite 'powershell-compilation-synthetic-loop' -OutputRoot $outputRoot {
    Add-BenchmarkMetadata Workload 'Typed triangular-number loop'
    Add-BenchmarkMetadata TypedArtifactSha256 $typedHash
    Add-BenchmarkMetadata BinaryModuleSha256 $moduleHash
    Set-BenchmarkPolicy -Warmup $warmup -Iterations $iterations -Order GroupedRotated -OutlierMode ExcludeMinMax
    Add-BenchmarkCase Loop @{ Calls = $loopCalls; Count = 1000; Expected = 500500L }

    Set-BenchmarkSetup {
        param($case, $run)
        . $workloadPath
        Set-Item -Path Function:\global:Get-TriangularNumber -Value ${function:Get-TriangularNumber}
        Import-Module -Name $moduleResult.ArtifactPath -Global -Force -ErrorAction Stop
    }

    Add-BenchmarkEngine PowerShellFunction {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            [long] $result = 0
            for ([int] $index = 0; $index -lt $case.Calls; $index++) {
                $result = Get-TriangularNumber $case.Count
            }
            $run.Result = $result
        }
    }

    Add-BenchmarkEngine BinaryCmdlet {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            [long] $result = 0
            for ([int] $index = 0; $index -lt $case.Calls; $index++) {
                $result = & "$moduleQualifier\Get-TriangularNumber" $case.Count
            }
            $run.Result = $result
        }
    }

    Add-BenchmarkEngine TypedClr {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunTypedLoop($case.Calls, $case.Count)
        }
    }

    Add-BenchmarkEngine HandWrittenCSharp {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunHandWrittenLoop($case.Calls, $case.Count)
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)
        Assert-BenchmarkValue -Actual ([long] $run.Result) -Expected ([long] $case.Expected)
    }
    Add-BenchmarkComparison -Dimension Engine -Baseline HandWrittenCSharp -Metric MedianMs -TieTolerance 0.05
}

New-BenchmarkSuite 'powershell-compilation-indexed-array' -OutputRoot $outputRoot {
    Add-BenchmarkMetadata Workload 'Typed indexed traversal over an Int32 array'
    Add-BenchmarkMetadata TypedArtifactSha256 $typedHash
    Add-BenchmarkMetadata BinaryModuleSha256 $moduleHash
    Set-BenchmarkPolicy -Warmup $warmup -Iterations $iterations -Order GroupedRotated -OutlierMode ExcludeMinMax
    Add-BenchmarkCase Indexed @{ Calls = $loopCalls; Values = [int[]](1..1000); Expected = 500500L }

    Set-BenchmarkSetup {
        param($case, $run)
        . $workloadPath
        Set-Item -Path Function:\global:Get-IndexedSum -Value ${function:Get-IndexedSum}
        Import-Module -Name $moduleResult.ArtifactPath -Global -Force -ErrorAction Stop
    }

    Add-BenchmarkEngine PowerShellFunction {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            [long] $result = 0
            for ([int] $call = 0; $call -lt $case.Calls; $call++) {
                $result = Get-IndexedSum $case.Values
            }
            $run.Result = $result
        }
    }

    Add-BenchmarkEngine BinaryCmdlet {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            [long] $result = 0
            for ([int] $call = 0; $call -lt $case.Calls; $call++) {
                $result = & "$moduleQualifier\Get-IndexedSum" $case.Values
            }
            $run.Result = $result
        }
    }

    Add-BenchmarkEngine TypedClr {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunTypedIndexedSum($case.Calls, $case.Values)
        }
    }

    Add-BenchmarkEngine HandWrittenCSharp {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunHandWrittenIndexedSum($case.Calls, $case.Values)
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)
        Assert-BenchmarkValue -Actual ([long] $run.Result) -Expected ([long] $case.Expected)
    }
    Add-BenchmarkComparison -Dimension Engine -Baseline HandWrittenCSharp -Metric MedianMs -TieTolerance 0.05
}

New-BenchmarkSuite 'powershell-compilation-binary-dispatch-amortization' -OutputRoot $outputRoot {
    Add-BenchmarkMetadata Workload 'Equivalent triangular-number work through fine or coarse generated commands'
    Add-BenchmarkMetadata BinaryModuleSha256 $moduleHash
    Set-BenchmarkPolicy -Warmup $warmup -Iterations $iterations -Order GroupedRotated -OutlierMode ExcludeMinMax
    Add-BenchmarkCase Loop @{ Calls = $loopCalls; Count = 1000; Expected = 500500L }

    Set-BenchmarkSetup {
        param($case, $run)
        . $workloadPath
        Set-Item -Path Function:\global:Get-TriangularNumber -Value ${function:Get-TriangularNumber}
        Set-Item -Path Function:\global:Get-RepeatedTriangularNumber -Value ${function:Get-RepeatedTriangularNumber}
        Import-Module -Name $moduleResult.ArtifactPath -Global -Force -ErrorAction Stop
    }

    Add-BenchmarkEngine FineBinaryCmdlet {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            [long] $result = 0
            for ([int] $index = 0; $index -lt $case.Calls; $index++) {
                $result = & "$moduleQualifier\Get-TriangularNumber" $case.Count
            }
            $run.Result = $result
        }
    }

    Add-BenchmarkEngine CoarseBinaryCmdlet {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = & "$moduleQualifier\Get-RepeatedTriangularNumber" $case.Calls $case.Count
        }
    }

    Add-BenchmarkEngine TypedClr {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunTypedRepeatedLoop($case.Calls, $case.Count)
        }
    }

    Add-BenchmarkEngine HandWrittenCSharp {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = [PowerForge.CompilationBenchmarks.PowerShellCompilationBenchmarkHarness]::RunHandWrittenRepeatedLoop($case.Calls, $case.Count)
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)
        Assert-BenchmarkValue -Actual ([long] $run.Result) -Expected ([long] $case.Expected)
    }
    Add-BenchmarkComparison -Dimension Engine -Baseline HandWrittenCSharp -Metric MedianMs -TieTolerance 0.05
}

New-BenchmarkSuite 'powershell-compilation-packaged-startup' -OutputRoot $outputRoot {
    Add-BenchmarkMetadata Workload 'Cold process startup and one script invocation'
    Add-BenchmarkMetadata PackagedExecutableSha256 $executableHash
    Add-BenchmarkMetadata TypedExecutableSha256 $typedExecutableHash
    Add-BenchmarkMetadata PowerShellScriptBytes (Get-Item -LiteralPath $startupScriptPath).Length
    Add-BenchmarkMetadata PackagedExecutableBytes (Get-Item -LiteralPath $executableResult.ArtifactPath).Length
    Add-BenchmarkMetadata TypedExecutableBytes (Get-Item -LiteralPath $typedExecutableResult.ArtifactPath).Length
    Add-BenchmarkMetadata OptimizedExecutableRuntimeIdentifier $runtimeIdentifier
    foreach ($entry in $optimizedExecutableEvidence.GetEnumerator()) {
        Add-BenchmarkMetadata "$($entry.Key)ExecutableSha256" $entry.Value.Sha256
        Add-BenchmarkMetadata "$($entry.Key)ExecutableBytes" $entry.Value.Bytes
    }
    Set-BenchmarkPolicy -Warmup 2 -Iterations $(if ($quick) { 3 } else { 10 }) -Order Rotated -OutlierMode ExcludeMinMax
    Add-BenchmarkCase Startup @{ Expected = 150.0 }

    Add-BenchmarkEngine PowerShellFile {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = & $currentPowerShell -NoProfile -NonInteractive -File $startupScriptPath 100 0.5 30
        }
    }

    Add-BenchmarkEngine PackagedExecutable {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = & $executableResult.ArtifactPath 100 0.5 30
        }
    }


    Add-BenchmarkEngine TypedExecutable {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = & $typedExecutableResult.ArtifactPath 100 0.5 30
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)
        Assert-BenchmarkValue -Actual ([double] $run.Result) -Expected ([double] $case.Expected)
    }
    Add-BenchmarkComparison -Dimension Engine -Baseline PowerShellFile -Metric MedianMs -TieTolerance 0.05
}

New-BenchmarkSuite 'powershell-compilation-typed-local-calls' -OutputRoot $outputRoot {
    Add-BenchmarkMetadata Workload 'Repeated local function calls through a multi-file Strict executable'
    Add-BenchmarkMetadata TypedExecutableSha256 $localCallExecutableHash
    Add-BenchmarkMetadata TypedExecutableBytes (Get-Item -LiteralPath $localCallResult.ArtifactPath).Length
    Set-BenchmarkPolicy -Warmup 2 -Iterations $(if ($quick) { 3 } else { 10 }) -Order Rotated -OutlierMode ExcludeMinMax
    Add-BenchmarkCase LocalCalls @{ Calls = $(if ($quick) { 2000 } else { 20000 }); Expected = $(if ($quick) { 2000L } else { 20000L }) }

    Add-BenchmarkEngine PowerShellFile {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = & $currentPowerShell -NoProfile -NonInteractive -File $localCallEntryPath $case.Calls
        }
    }

    Add-BenchmarkEngine TypedExecutable {
        Add-BenchmarkOperation Invoke {
            param($case, $run)
            $run.Result = & $localCallResult.ArtifactPath $case.Calls
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)
        Assert-BenchmarkValue -Actual ([long] $run.Result) -Expected ([long] $case.Expected)
    }
    Add-BenchmarkComparison -Dimension Engine -Baseline PowerShellFile -Metric MedianMs -TieTolerance 0.05
}
