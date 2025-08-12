# Module Testing Functions

## Overview

PSPublishModule now includes a comprehensive set of functions for testing PowerShell modules. These functions automate the process of dependency management, module importing, and test execution, making it easy to create consistent testing workflows across projects.

## Quick Start

For 99% of users, you just need one command:

```powershell
# Test your module - that's it!
Invoke-ModuleTestSuite
```

This single command automatically:
- Finds your module manifest (.psd1 file)
- Installs missing dependencies
- Imports your module
- Runs all Pester tests
- Shows comprehensive results

## When You Need More

Only use the individual functions if you need custom control over each step:

```powershell
# Step-by-step approach (advanced users only)
$moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
Test-RequiredModules -ModuleInformation $moduleInfo
Test-ModuleImport -ModuleInformation $moduleInfo
$results = Invoke-ModuleTests -ModuleInformation $moduleInfo -PassThru
```

## Functions

### `Invoke-ModuleTestSuite`
The primary function that orchestrates the complete testing workflow.

**Features:**
- Automatic module manifest detection
- Dependency management (install missing modules)
- Module importing with error handling
- Pester test execution with configuration
- Comprehensive reporting

**Basic Usage:**
```powershell
# Test current module with defaults
Invoke-ModuleTestSuite

# Test with additional modules and coverage
Invoke-ModuleTestSuite -AdditionalModules @('PSScriptAnalyzer') -EnableCodeCoverage
```

### `Get-ModuleInformation`
Retrieves and validates module manifest information.

**Usage:**
```powershell
$moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
Write-Host "Module: $($moduleInfo.ModuleName) v$($moduleInfo.ModuleVersion)"
```

### `Test-RequiredModules`
Manages module dependencies with version checking.

**Features:**
- Installs missing modules
- Validates version requirements
- Supports RequiredVersion, MinimumVersion, MaximumVersion
- Skip modules option

**Usage:**
```powershell
$moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
Test-RequiredModules -ModuleInformation $moduleInfo -AdditionalModules @('Pester') -SkipModules @('CertNoob')
```

### `Test-ModuleImport`
Imports modules with detailed error reporting.

**Usage:**
```powershell
# Import using module info
Test-ModuleImport -ModuleInformation $moduleInfo -ShowInformation

# Import by path
Test-ModuleImport -Path "C:\MyModule\MyModule.psd1"

# Import by name
Test-ModuleImport -ModuleName "MyModule"
```

### `Invoke-ModuleTests`
Executes Pester tests with comprehensive configuration.

**Features:**
- Supports both Pester v4 and v5+
- Code coverage analysis
- Multiple output formats
- Detailed error reporting

**Usage:**
```powershell
$results = Invoke-ModuleTests -ModuleInformation $moduleInfo -EnableCodeCoverage -PassThru
Write-Host "Coverage: $($results.CodeCoverage.CoveragePercent)%"
```

## Configuration Options

### Output Formats
- **Detailed**: Full verbose output with all test details
- **Normal**: Standard output with summary information
- **Minimal**: Minimal output for CI/CD scenarios

### Code Coverage
Enable with `-EnableCodeCoverage` switch. Works with both Pester v4 and v5+.

### Module Management
- **AdditionalModules**: Add modules beyond manifest requirements
- **SkipModules**: Skip specific modules during installation
- **Force**: Force reinstallation/reimport of modules

## Examples

### Simple Testing
```powershell
# Most basic usage
Invoke-ModuleTestSuite
```

### Advanced Testing
```powershell
$results = Invoke-ModuleTestSuite -ProjectPath $PSScriptRoot `
    -AdditionalModules @('Pester', 'PSWriteColor', 'PSScriptAnalyzer') `
    -SkipModules @('CertNoob') `
    -EnableCodeCoverage `
    -OutputFormat Detailed `
    -PassThru

Write-Host "Tests: $($results.PassedCount)/$($results.TotalCount)"
```

### CI/CD Integration
```powershell
# Simple CI/CD - automatically exits on failure
Invoke-ModuleTestSuite -CICD -EnableCodeCoverage

# Advanced CI/CD with custom result handling
$results = Invoke-ModuleTestSuite -CICD -EnableCodeCoverage
# Process $results as needed
```

### Individual Function Control
```powershell
# Step-by-step control
$moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
Test-RequiredModules -ModuleInformation $moduleInfo -AdditionalModules @('Pester')
Test-ModuleImport -ModuleInformation $moduleInfo -ShowInformation
$results = Invoke-ModuleTests -ModuleInformation $moduleInfo -EnableCodeCoverage -PassThru
```

## Template for New Projects

Copy `Data/Template-ModuleTests.ps1` to your project and customize as needed:

```powershell
# Ensure PSPublishModule is installed
if (-not (Get-Module -ListAvailable -Name PSPublishModule)) {
    Install-Module -Name PSPublishModule -Force -SkipPublisherCheck
}

Import-Module PSPublishModule -Force

# Run tests
Invoke-ModuleTestSuite -EnableCodeCoverage
```

## Version Support

- **PowerShell**: 5.1 and 7+
- **Pester**: Automatically detects and works with v4 and v5+
- **Platforms**: Windows, macOS, Linux

## Error Handling

All functions include comprehensive error handling with:
- Detailed error messages
- Stack trace information
- Context-specific troubleshooting hints
- Graceful failure modes

## Return Values

Functions return structured objects with:
- Test results and statistics
- Module information
- Version comparison details
- Error context when needed

Use `-PassThru` with test functions to capture results for further processing.
