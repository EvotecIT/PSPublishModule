# Example: Individual Function Control
# This example shows using individual functions for maximum control
# (Most users should just use Invoke-ModuleTestSuite instead)

# Step 1: Get module information
$moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
Write-Host "Found module: $($moduleInfo.ModuleName) v$($moduleInfo.ModuleVersion)" -ForegroundColor Cyan

# Step 2: Check and install dependencies
Test-RequiredModules -ModuleInformation $moduleInfo -AdditionalModules @('Pester', 'PSScriptAnalyzer') -SkipModules @('CertNoob')

# Step 3: Import the module under test
Test-ModuleImport -ModuleInformation $moduleInfo -ShowInformation

# Step 4: Run tests with custom configuration
$testResults = Invoke-ModuleTests -ModuleInformation $moduleInfo -OutputFormat Normal -EnableCodeCoverage -PassThru

# Step 5: Process results
Write-Host "`nCustom test run completed!" -ForegroundColor Green
Write-Host "Passed: $($testResults.PassedCount) | Failed: $($testResults.FailedCount)" -ForegroundColor White

# Note: This approach gives you maximum control but is more complex
# For most scenarios, just use: Invoke-ModuleTestSuite
