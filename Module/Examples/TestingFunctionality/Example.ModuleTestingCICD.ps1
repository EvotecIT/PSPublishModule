# Example: Simple CI/CD Pipeline Integration
# This example shows the simplest way to integrate testing into CI/CD pipelines

# For CI/CD - just use the -CICD switch for optimized settings
Invoke-ModuleTestSuite -CICD -EnableCodeCoverage

# That's it! The -CICD switch automatically:
# - Uses minimal output for clean logs
# - Exits with proper code (0 = success, 1 = failure)
# - Sets environment variables for GitHub Actions & Azure DevOps
# - Finds your module, installs dependencies, imports module, runs tests
