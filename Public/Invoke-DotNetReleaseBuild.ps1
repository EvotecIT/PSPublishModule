function Invoke-DotNetReleaseBuild {
    <#
    .SYNOPSIS
    Builds a .NET project in Release configuration and prepares release artefacts.

    .DESCRIPTION
    Wrapper around the build, pack and signing process typically used for publishing
    .NET projects. The function cleans the Release directory, builds the project,
    signs DLLs and NuGet packages when a certificate is provided, compresses the
    build output and returns details about the generated files.

    .PARAMETER ProjectPath
    Path to the folder containing the project (*.csproj) file.

    .PARAMETER CertificateThumbprint
    Optional certificate thumbprint used to sign the built assemblies and NuGet
    packages. When omitted no signing is performed.

    .PARAMETER LocalStore
    Certificate store used when searching for the signing certificate. Defaults
    to 'CurrentUser'.

    .PARAMETER TimeStampServer
    Timestamp server URL used while signing.

    .PARAMETER PackDependencies
    When enabled, also packs all project dependencies that have their own .csproj files.
    This is useful for multi-project solutions where you want to create NuGet packages
    for all related projects in one command.

    .OUTPUTS
    PSCustomObject with properties Version, ReleasePath and ZipPath.

    .EXAMPLE
    Invoke-DotNetReleaseBuild -ProjectPath 'C:\Git\MyProject' -CertificateThumbprint '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703'
    Builds and signs the project located in C:\Git\MyProject and returns paths to
    the release output.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$ProjectPath,
        [Parameter()]
        [string]$CertificateThumbprint,
        [string]$LocalStore = 'CurrentUser',
        [string]$TimeStampServer = 'http://timestamp.digicert.com',
        [switch]$PackDependencies
    )
    $result = [ordered]@{
        Success      = $false
        Version      = $null
        ReleasePath  = $null
        ZipPath      = $null
        Packages     = @()
        DependencyProjects = @()
        ErrorMessage = $null
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        $result.ErrorMessage = 'dotnet CLI is not available.'
        return [PSCustomObject]$result
    }
    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        $result.ErrorMessage = "Project path '$ProjectPath' not found."
        return [PSCustomObject]$result
    }
    $csproj = Get-ChildItem -Path $ProjectPath -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $csproj) {
        $result.ErrorMessage = "No csproj found in $ProjectPath"
        return [PSCustomObject]$result
    }
    try {
        [xml]$xml = Get-Content -LiteralPath $csproj.FullName -Raw -ErrorAction Stop
    } catch {
        $result.ErrorMessage = "Failed to read '$($csproj.FullName)' as XML: $_"
        return [PSCustomObject]$result
    }
    $version = ($xml.Project.PropertyGroup | Where-Object { $_.VersionPrefix } | Select-Object -First 1).VersionPrefix
    if (-not $version) {
        $result.ErrorMessage = "VersionPrefix not found in '$($csproj.FullName)'"
        return [PSCustomObject]$result
    }

    # Find dependency projects if PackDependencies is specified
    $dependencyProjects = @()
    if ($PackDependencies) {
        $itemGroups = $xml.Project.ItemGroup
        if ($itemGroups) {
            foreach ($itemGroup in $itemGroups) {
                if ($itemGroup.ProjectReference) {
                    foreach ($ref in $itemGroup.ProjectReference) {
                        if ($ref.Include) {
                            $depPath = Join-Path (Split-Path $csproj.FullName) $ref.Include
                            if (Test-Path $depPath) {
                                $dependencyProjects += $depPath
                            }
                        }
                    }
                }
            }
        }
        $result.DependencyProjects = $dependencyProjects
    }

    $releasePath = Join-Path -Path $csproj.Directory.FullName -ChildPath 'bin/Release'

    if ($PSCmdlet.ShouldProcess("$($csproj.BaseName) v$version", "Build and pack .NET project")) {
        if (Test-Path -LiteralPath $releasePath) {
        try {
            Get-ChildItem -Path $releasePath -Recurse -File | Remove-Item -Force
            Get-ChildItem -Path $releasePath -Recurse -Filter '*.nupkg' | Remove-Item -Force
            Get-ChildItem -Path $releasePath -Directory | Remove-Item -Force -Recurse
        } catch {
            $result.ErrorMessage = "Failed to clean $($releasePath): $_"
            return [PSCustomObject]$result
        }
    } else {
        $null = New-Item -ItemType Directory -Path $releasePath -Force
    }

    dotnet build $csproj.FullName --configuration Release
    if ($LASTEXITCODE -ne 0) {
        $result.ErrorMessage = 'dotnet build failed.'
        return [PSCustomObject]$result
    }
    if ($CertificateThumbprint) {
        Register-Certificate -Path $releasePath -LocalStore $LocalStore -Include @('*.dll') -TimeStampServer $TimeStampServer -Thumbprint $CertificateThumbprint
    }
    $zipPath = Join-Path -Path $releasePath -ChildPath ("{0}.{1}.zip" -f $csproj.BaseName, $version)
    Compress-Archive -Path (Join-Path $releasePath '*') -DestinationPath $zipPath -Force

    # Pack the main project
    dotnet pack $csproj.FullName --configuration Release --no-restore --no-build
    if ($LASTEXITCODE -ne 0) {
        $result.ErrorMessage = 'dotnet pack failed.'
        return [PSCustomObject]$result
    }

    # Pack dependency projects if requested
    if ($PackDependencies -and $dependencyProjects.Count -gt 0) {
        Write-Verbose "Invoke-DotNetReleaseBuild - Packing $($dependencyProjects.Count) dependency projects"
        foreach ($depProj in $dependencyProjects) {
            Write-Verbose "Invoke-DotNetReleaseBuild - Packing dependency: $(Split-Path -Leaf $depProj)"
            dotnet pack $depProj --configuration Release --no-restore --no-build
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Invoke-DotNetReleaseBuild - Failed to pack dependency: $(Split-Path -Leaf $depProj)"
            }
        }
    }

    # Collect all packages from main project and dependencies
    $allPackages = @()

    # Main project packages
    $nupkgs = Get-ChildItem -Path $releasePath -Recurse -Filter '*.nupkg' -ErrorAction SilentlyContinue
    $allPackages += $nupkgs.FullName

    # Dependency project packages
    if ($PackDependencies) {
        foreach ($depProj in $dependencyProjects) {
            $depReleasePath = Join-Path (Split-Path $depProj) 'bin\Release'
            if (Test-Path $depReleasePath) {
                $depNupkgs = Get-ChildItem -Path $depReleasePath -Recurse -Filter '*.nupkg' -ErrorAction SilentlyContinue
                $allPackages += $depNupkgs.FullName
            }
        }
    }

    # Sign all packages
    if ($CertificateThumbprint -and $allPackages.Count -gt 0) {
        foreach ($pkgPath in $allPackages) {
            dotnet nuget sign $pkgPath --certificate-fingerprint $CertificateThumbprint --timestamper $TimeStampServer --overwrite
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Invoke-DotNetReleaseBuild - Failed to sign $pkgPath"
            }
        }
    }

        $result.Success = $true
        $result.Packages = $allPackages
    } else {
        # WhatIf mode - return simulated results
        $result.Success = $true
        $result.Packages = @(Join-Path $releasePath ("{0}.{1}.nupkg" -f $csproj.BaseName, $version))
        $zipPath = Join-Path $releasePath ("{0}.{1}.zip" -f $csproj.BaseName, $version)

        if ($PackDependencies -and $dependencyProjects.Count -gt 0) {
            Write-Host "Would also pack $($dependencyProjects.Count) dependency projects:" -ForegroundColor Yellow
            foreach ($depProj in $dependencyProjects) {
                Write-Host "  - $(Split-Path -Leaf $depProj)" -ForegroundColor Yellow
            }
        }
    }

    $result.Version = $version
    $result.ReleasePath = $releasePath
    $result.ZipPath = $zipPath
    return [PSCustomObject]$result
}
