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

    .OUTPUTS
    PSCustomObject with properties Version, ReleasePath and ZipPath.

    .EXAMPLE
    Invoke-DotNetReleaseBuild -ProjectPath 'C:\Git\MyProject' -CertificateThumbprint '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703'
    Builds and signs the project located in C:\Git\MyProject and returns paths to
    the release output.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$ProjectPath,
        [Parameter()]
        [string]$CertificateThumbprint,
        [string]$LocalStore = 'CurrentUser',
        [string]$TimeStampServer = 'http://timestamp.digicert.com'
    )
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error 'Invoke-DotNetReleaseBuild - dotnet CLI is not available.'
        return
    }
    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        Write-Error "Invoke-DotNetReleaseBuild - Project path '$ProjectPath' not found."
        return
    }
    $csproj = Get-ChildItem -Path $ProjectPath -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $csproj) {
        Write-Error "Invoke-DotNetReleaseBuild - No csproj found in $ProjectPath"
        return
    }
    [xml]$xml = Get-Content -LiteralPath $csproj.FullName -Raw
    $version = $xml.Project.PropertyGroup.VersionPrefix
    $releasePath = Join-Path -Path $csproj.Directory.FullName -ChildPath 'bin/Release'
    if (Test-Path -LiteralPath $releasePath) {
        try {
            Get-ChildItem -Path $releasePath -Recurse -File | Remove-Item -Force
            Get-ChildItem -Path $releasePath -Recurse -Filter '*.nupkg' | Remove-Item -Force
            Get-ChildItem -Path $releasePath -Directory | Remove-Item -Force -Recurse
        } catch {
            Write-Warning "Invoke-DotNetReleaseBuild - Failed to clean $releasePath: $_"
            return
        }
    } else {
        $null = New-Item -ItemType Directory -Path $releasePath -Force
    }

    dotnet build $csproj.FullName --configuration Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error 'Invoke-DotNetReleaseBuild - dotnet build failed.'
        return
    }
    if ($CertificateThumbprint) {
        Register-Certificate -Path $releasePath -LocalStore $LocalStore -Include @('*.dll') -TimeStampServer $TimeStampServer -Thumbprint $CertificateThumbprint
    }
    $zipPath = Join-Path -Path $releasePath -ChildPath ("{0}.{1}.zip" -f $csproj.BaseName, $version)
    Compress-Archive -Path (Join-Path $releasePath '*') -DestinationPath $zipPath -Force

    dotnet pack $csproj.FullName --configuration Release --no-restore --no-build
    if ($LASTEXITCODE -ne 0) {
        Write-Error 'Invoke-DotNetReleaseBuild - dotnet pack failed.'
        return
    }
    if ($CertificateThumbprint) {
        $nupkgs = Get-ChildItem -Path $releasePath -Recurse -Filter '*.nupkg' -ErrorAction SilentlyContinue
        foreach ($pkg in $nupkgs) {
            dotnet nuget sign $pkg.FullName --certificate-fingerprint $CertificateThumbprint --timestamper $TimeStampServer --overwrite
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Invoke-DotNetReleaseBuild - Failed to sign $($pkg.FullName)"
            }
        }
    }
    [PSCustomObject]@{
        Version     = $version
        ReleasePath = $releasePath
        ZipPath     = $zipPath
    }
}
