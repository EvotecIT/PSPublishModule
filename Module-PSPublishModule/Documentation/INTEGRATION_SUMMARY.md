# PSPublishModule Test Failure Analysis Integration

## Overview

This integration adds comprehensive test failure analysis capabilities to the PSPublishModule framework. The new functionality provides detailed insights into Pester test failures with multiple output formats and seamless integration with existing testing workflows.

## New Function: Get-ModuleTestFailures

### Purpose
- Analyzes and summarizes failed Pester tests from various sources
- Supports both NUnit XML files and Pester result objects
- Provides multiple output formats for different use cases
- Integrates seamlessly with existing PSPublishModule testing framework

### Key Features
- **Multiple Input Sources**: Works with Pester result objects or NUnit XML files
- **Flexible Output Formats**: Summary, Detailed, and JSON output options
- **Automatic Discovery**: Finds test results in standard project locations
- **Version Compatibility**: Supports both Pester v4 and v5+ formats
- **CI/CD Ready**: JSON output perfect for automated processing

## Integration Points

### 1. Invoke-ModuleTestSuite Enhancement

**New Parameters:**
- `ShowFailureSummary`: Automatically display failure analysis when tests fail
- `FailureSummaryFormat`: Choose between 'Summary' or 'Detailed' formats

**Updated Examples:**
```powershell
# Basic usage with failure analysis
Invoke-ModuleTestSuite -ShowFailureSummary

# Detailed failure analysis
Invoke-ModuleTestSuite -ShowFailureSummary -FailureSummaryFormat Detailed

# CI/CD mode automatically includes failure analysis
Invoke-ModuleTestSuite -CICD
```

### 2. Standalone Usage

```powershell
# Analyze from test results object (recommended)
$testResults = Invoke-ModuleTestSuite -PassThru
Get-ModuleTestFailures -TestResults $testResults

# Analyze from XML file
Get-ModuleTestFailures -Path 'Tests\TestResults.xml'

# Different output formats
Get-ModuleTestFailures -OutputFormat Summary
Get-ModuleTestFailures -OutputFormat Detailed
Get-ModuleTestFailures -OutputFormat JSON

# Get results for processing
$failures = Get-ModuleTestFailures -PassThru
```

## Output Formats

### Summary Format
- Test counts with visual indicators (✅ ❌ ⏭️)
- Success rate percentage
- Basic failed test list
- Color-coded for easy reading

### Detailed Format
- Complete failure information with error messages
- Test duration (when available)
- Stack traces for debugging
- Comprehensive analysis report

### JSON Format
- Machine-readable output
- Perfect for CI/CD integration
- Includes all analysis data
- Easy to parse and process

## Files Added/Modified

### New Files
1. `Public\Get-ModuleTestFailures.ps1` - Main function implementation
2. `Docs\Get-ModuleTestFailures.md` - Comprehensive documentation
3. `Tests\Get-ModuleTestFailures.Tests.ps1` - Unit tests
4. `Examples\TestingFunctionality\Example.TestFailureAnalysis.ps1` - Usage examples

### Modified Files
1. `Public\Invoke-ModuleTestSuite.ps1` - Added failure analysis integration
2. `PSPublishModule.psd1` - Added new function to exports
3. `Examples\TestingFunctionality\Example.ModuleTestingAdvanced.ps1` - Updated examples

## Usage Examples

### Basic Integration
```powershell
# Run tests with automatic failure analysis
Invoke-ModuleTestSuite -ShowFailureSummary -FailureSummaryFormat Detailed
```

### CI/CD Pipeline
```powershell
# CI/CD mode with comprehensive reporting
$results = Invoke-ModuleTestSuite -CICD -PassThru

# Export failure details for processing
if ($results.FailedCount -gt 0) {
    $failures = Get-ModuleTestFailures -TestResults $results -OutputFormat JSON
    $failures | Out-File 'test-failures.json'
}
```

### Custom Processing
```powershell
# Get detailed failure analysis
$failures = Get-ModuleTestFailures -TestResults $testResults -PassThru

# Process each failure
foreach ($failure in $failures.FailedTests) {
    Write-Host "❌ $($failure.Name)" -ForegroundColor Red
    if ($failure.ErrorMessage -ne 'No error message available') {
        Write-Host "   Error: $($failure.ErrorMessage)" -ForegroundColor Yellow
    }
    if ($failure.Duration) {
        Write-Host "   Duration: $($failure.Duration)" -ForegroundColor Gray
    }
}
```

## Benefits

1. **Improved Debugging**: Clear, formatted failure information helps identify issues quickly
2. **CI/CD Integration**: JSON output and automated analysis perfect for build pipelines
3. **Flexible Usage**: Works standalone or integrated with existing workflows
4. **Version Compatibility**: Supports both old and new Pester versions
5. **Comprehensive Coverage**: Handles both XML files and result objects
6. **Professional Output**: Color-coded, emoji-enhanced output for better readability

## Best Practices

1. **Use with PassThru**: Always get test results when you need failure analysis
   ```powershell
   $results = Invoke-ModuleTestSuite -PassThru
   Get-ModuleTestFailures -TestResults $results
   ```

2. **CI/CD Integration**: Use CICD mode for automated builds
   ```powershell
   Invoke-ModuleTestSuite -CICD  # Automatically includes failure analysis
   ```

3. **JSON for Automation**: Use JSON format for programmatic processing
   ```powershell
   $json = Get-ModuleTestFailures -OutputFormat JSON
   ```

4. **Detailed for Development**: Use detailed format during development
   ```powershell
   Get-ModuleTestFailures -OutputFormat Detailed
   ```

This integration maintains backward compatibility while adding powerful new capabilities for test failure analysis, making it easier to identify, understand, and resolve test failures in PowerShell module development.