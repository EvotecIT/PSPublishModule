# Module Testing Examples

This folder contains examples showing different ways to use the PSPublishModule testing functions.

## Quick Reference

### Simple Testing (99% of users)
```powershell
# Just run this - it does everything automatically
Invoke-ModuleTestSuite
```

### CI/CD Integration
```powershell
# For automated pipelines
Invoke-ModuleTestSuite -CICD -EnableCodeCoverage
```

## Example Files

1. **Example.ModuleTestingBasic.ps1** - Basic usage, one command does everything
2. **Example.ModuleTestingAdvanced.ps1** - Custom options but still one command
3. **Example.ModuleTestingCICD.ps1** - Simple CI/CD integration
4. **Example.ModuleTestingCICDAdvanced.ps1** - CI/CD with custom result processing
5. **Example.ModuleTestingIndividualFunctions.ps1** - Using individual functions for maximum control

## Which Example Should I Use?

- **Most users**: Start with `Example.ModuleTestingBasic.ps1`
- **Need custom modules/settings**: Use `Example.ModuleTestingAdvanced.ps1`
- **CI/CD pipeline**: Use `Example.ModuleTestingCICD.ps1`
- **Advanced CI/CD**: Use `Example.ModuleTestingCICDAdvanced.ps1`
- **Need maximum control**: Use `Example.ModuleTestingIndividualFunctions.ps1`

## Key Point

The main function `Invoke-ModuleTestSuite` does everything automatically:
- Finds your module manifest
- Installs missing dependencies
- Imports your module
- Runs all tests
- Shows results

You only need the individual functions (`Get-ModuleInformation`, `Test-RequiredModules`, etc.) if you want to customize each step separately.