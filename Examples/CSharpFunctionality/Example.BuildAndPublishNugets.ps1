# ImagePlayground Build Script - Using Enhanced PSPublishModule Functions
# Now with proper ShouldProcess/WhatIf support!

Import-Module PSPublishModule -Force -ErrorAction Stop

# Test the build process first
Write-Host "=== Testing Build Process (WhatIf) ===" -ForegroundColor Green
$testResult = Invoke-DotNetReleaseBuild -ProjectPath "$PSScriptRoot\..\..\ImagePlayground\Sources\ImagePlayground" -PackDependencies -WhatIf

if ($testResult.Success) {
    Write-Host "✅ Build test passed!" -ForegroundColor Green
    Write-Host "Main project: $($testResult.Version)" -ForegroundColor Gray
    Write-Host "Dependencies found: $($testResult.DependencyProjects.Count)" -ForegroundColor Gray

    # Show what would be built
    foreach ($dep in $testResult.DependencyProjects) {
        Write-Host "  - $(Split-Path -Leaf $dep)" -ForegroundColor DarkGray
    }
} else {
    Write-Host "❌ Build test failed: $($testResult.ErrorMessage)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Testing Publishing (WhatIf) ===" -ForegroundColor Green

# Test GitHub publishing
$githubTest = Publish-GitHubReleaseAsset -ProjectPath "$PSScriptRoot\..\..\ImagePlayground\Sources\ImagePlayground" -GitHubUsername "EvotecIT" -GitHubRepositoryName "ImagePlayground" -GitHubAccessToken "dummy" -WhatIf
Write-Host "GitHub publish test: $(if ($githubTest.Success) { "✅ OK" } else { "❌ Failed" })" -ForegroundColor $(if ($githubTest.Success) { "Green" } else { "Red" })

# Test NuGet publishing
$nugetTest = Publish-NugetPackage -Path "$PSScriptRoot\..\..\ImagePlayground\Sources\ImagePlayground\bin\Release" -ApiKey "dummy" -WhatIf
Write-Host "NuGet publish test: $(if ($nugetTest.Success) { "✅ OK" } else { "❌ Failed" })" -ForegroundColor $(if ($nugetTest.Success) { "Green" } else { "Red" })

Write-Host ""
Write-Host "=== Ready for Actual Execution ===" -ForegroundColor Green
Write-Host "To execute for real, uncomment the sections below and provide real credentials:" -ForegroundColor Yellow

# Uncomment and modify the following sections when ready to execute

<#
# Step 1: Build with dependency packing
Write-Host "🔨 Building ImagePlayground with all dependencies..." -ForegroundColor Cyan
$buildResult = Invoke-DotNetReleaseBuild -ProjectPath "$PSScriptRoot\..\..\ImagePlayground\Sources\ImagePlayground" -PackDependencies -CertificateThumbprint '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703'

if ($buildResult.Success) {
    Write-Host "✅ Build successful!" -ForegroundColor Green
    Write-Host "Packages created: $($buildResult.Packages.Count)" -ForegroundColor Gray

    foreach ($pkg in $buildResult.Packages) {
        Write-Host "  - $(Split-Path -Leaf $pkg)" -ForegroundColor DarkGray
    }

    # Step 2: Publish to GitHub
    $GitHubAccessToken = $env:GITHUB_TOKEN  # Or however you store it
    if ($GitHubAccessToken) {
        Write-Host "📤 Publishing to GitHub..." -ForegroundColor Cyan
        $githubResult = Publish-GitHubReleaseAsset -ProjectPath "$PSScriptRoot\..\..\ImagePlayground\Sources\ImagePlayground" -GitHubUsername "EvotecIT" -GitHubRepositoryName "ImagePlayground" -GitHubAccessToken $GitHubAccessToken

        if ($githubResult.Success) {
            Write-Host "✅ GitHub publish successful!" -ForegroundColor Green
            Write-Host "Release URL: $($githubResult.ReleaseUrl)" -ForegroundColor Gray
        } else {
            Write-Host "❌ GitHub publish failed: $($githubResult.ErrorMessage)" -ForegroundColor Red
        }
    } else {
        Write-Host "⚠️ No GitHub token found - skipping GitHub publish" -ForegroundColor Yellow
    }

    # Step 3: Publish to NuGet
    $NugetAPI = Get-Content -Raw -LiteralPath "C:\Support\Important\NugetOrgEvotec.txt" -ErrorAction SilentlyContinue
    if ($NugetAPI) {
        Write-Host "📦 Publishing to NuGet..." -ForegroundColor Cyan

        # Publish from main project's release path (contains all packages now)
        $nugetResult = Publish-NugetPackage -Path $buildResult.ReleasePath -ApiKey $NugetAPI

        if ($nugetResult.Success) {
            Write-Host "✅ NuGet publish successful!" -ForegroundColor Green
            Write-Host "Packages published: $($nugetResult.Pushed.Count)" -ForegroundColor Gray

            foreach ($pkg in $nugetResult.Pushed) {
                Write-Host "  ✅ $(Split-Path -Leaf $pkg)" -ForegroundColor Green
            }

            if ($nugetResult.Failed.Count -gt 0) {
                Write-Host "Failed packages: $($nugetResult.Failed.Count)" -ForegroundColor Red
                foreach ($pkg in $nugetResult.Failed) {
                    Write-Host "  ❌ $(Split-Path -Leaf $pkg)" -ForegroundColor Red
                }
            }
        } else {
            Write-Host "❌ NuGet publish failed: $($nugetResult.ErrorMessage)" -ForegroundColor Red
        }
    } else {
        Write-Host "⚠️ No NuGet API key found - skipping NuGet publish" -ForegroundColor Yellow
    }

} else {
    Write-Host "❌ Build failed: $($buildResult.ErrorMessage)" -ForegroundColor Red
}
#>

Write-Host ""
Write-Host "🎉 Script completed successfully!" -ForegroundColor Green