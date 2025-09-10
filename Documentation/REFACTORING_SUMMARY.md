# PSPublishModule Test Failure Analysis - Refactored Structure

## Overview
The test failure analysis functionality has been refactored to follow the PSPublishModule pattern of one function per file, with proper separation between public and private functions.

## File Structure

### Public Functions
- `Public\Get-ModuleTestFailures.ps1` - Main public function for analyzing test failures

### Private Functions
- `Private\Get-FailuresFromPesterResults.ps1` - Extracts failure information from Pester result objects
- `Private\Get-FailuresFromXmlFile.ps1` - Extracts failure information from NUnit XML files
- `Private\Write-ModuleTestSummary.ps1` - Displays summary format output
- `Private\Write-ModuleTestDetails.ps1` - Displays detailed format output

## Function Responsibilities

### Get-ModuleTestFailures (Public)
**Purpose**: Main entry point for test failure analysis
**Responsibilities**:
- Parameter validation and processing
- Path discovery for XML files
- Orchestrating calls to private functions
- Output format selection
- Error handling and user feedback

### Get-FailuresFromPesterResults (Private)
**Purpose**: Process Pester result objects
**Responsibilities**:
- Handle both Pester v4 and v5+ result formats
- Extract test counts and failure information
- Create standardized failure objects
- Support both Tests and TestResult properties

### Get-FailuresFromXmlFile (Private)
**Purpose**: Process NUnit XML files
**Responsibilities**:
- Parse XML test result files
- Extract test counts from XML attributes
- Process individual test case failures
- Handle XML structure variations

### Write-ModuleTestSummary (Private)
**Purpose**: Display summary format output
**Responsibilities**:
- Show test statistics with visual indicators
- Display success rates and counts
- List failed test names
- Provide concise overview

### Write-ModuleTestDetails (Private)
**Purpose**: Display detailed format output
**Responsibilities**:
- Show comprehensive failure information
- Include error messages and stack traces
- Display timing information
- Provide diagnostic details

## Benefits of Refactoring

1. **Maintainability**: Each function has a single responsibility
2. **Testability**: Private functions can be unit tested independently
3. **Reusability**: Private functions can be reused by other functions
4. **Code Organization**: Follows established PSPublishModule patterns
5. **Readability**: Smaller, focused functions are easier to understand

## Usage Examples

The public interface remains unchanged:

```powershell
# Basic usage
Get-ModuleTestFailures

# With test results object
$results = Invoke-ModuleTestSuite -PassThru
Get-ModuleTestFailures -TestResults $results

# Different output formats
Get-ModuleTestFailures -OutputFormat Summary
Get-ModuleTestFailures -OutputFormat Detailed
Get-ModuleTestFailures -OutputFormat JSON

# For processing
$analysis = Get-ModuleTestFailures -PassThru
```

## Integration Points

### Invoke-ModuleTestSuite Integration
The integration with `Invoke-ModuleTestSuite` remains unchanged:

```powershell
# Automatic failure analysis
Invoke-ModuleTestSuite -ShowFailureSummary

# With custom format
Invoke-ModuleTestSuite -ShowFailureSummary -FailureSummaryFormat Detailed

# CI/CD mode includes failure analysis
Invoke-ModuleTestSuite -CICD
```

## Testing

All functions maintain their original functionality:
- Unit tests still pass
- Integration tests work correctly
- Private functions are accessible within the module scope
- Public interface remains stable

## File Dependencies

```
Get-ModuleTestFailures (Public)
├── Get-FailuresFromPesterResults (Private)
├── Get-FailuresFromXmlFile (Private)
├── Write-ModuleTestSummary (Private)
└── Write-ModuleTestDetails (Private)
```

This refactored structure maintains all existing functionality while improving code organization and following PSPublishModule best practices.