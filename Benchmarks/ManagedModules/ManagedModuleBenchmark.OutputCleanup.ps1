function ConvertTo-BenchmarkFullPath {
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }

    [IO.Path]::GetFullPath($Path).TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
}

function Test-BenchmarkOutputRootOwned {
    param(
        [string] $Path,
        [string[]] $AllowedRoots
    )

    $candidate = ConvertTo-BenchmarkFullPath -Path $Path
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        return $false
    }

    $comparison = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }

    foreach ($root in @($AllowedRoots)) {
        $allowed = ConvertTo-BenchmarkFullPath -Path $root
        if ([string]::IsNullOrWhiteSpace($allowed)) {
            continue
        }

        if ([string]::Equals($candidate, $allowed, $comparison)) {
            return $false
        }

        $prefix = $allowed + [IO.Path]::DirectorySeparatorChar
        if ($candidate.StartsWith($prefix, $comparison)) {
            return $true
        }
    }

    $false
}

function Remove-ManagedModuleBenchmarkOutputRoots {
    param(
        [object[]] $Rows,
        [string[]] $AllowedRoots
    )

    $stringComparer = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        [StringComparer]::OrdinalIgnoreCase
    } else {
        [StringComparer]::Ordinal
    }
    $roots = [Collections.Generic.HashSet[string]]::new($stringComparer)

    foreach ($row in @($Rows)) {
        $outputRoot = [string] $row.OutputRoot
        if (-not (Test-BenchmarkOutputRootOwned -Path $outputRoot -AllowedRoots $AllowedRoots)) {
            continue
        }

        $fullPath = ConvertTo-BenchmarkFullPath -Path $outputRoot
        if (Test-Path -LiteralPath $fullPath -PathType Container) {
            $null = $roots.Add($fullPath)
        }
    }

    $removed = 0
    foreach ($root in @($roots) | Sort-Object Length -Descending) {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path -LiteralPath $root)) {
            $removed++
        }
    }

    $removed
}
