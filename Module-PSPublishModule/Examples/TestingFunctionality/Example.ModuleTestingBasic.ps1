# Example: Simple Module Testing
# This example shows the simplest way to use the new testing functions

# Basic usage - tests the current module with default settings
Invoke-ModuleTestSuite

# This single command will:
# 1. Find and load the module manifest (.psd1 file)
# 2. Install any missing required modules (Pester, PSWriteColor by default)
# 3. Import the module under test
# 4. Run all Pester tests in the Tests folder
# 5. Display comprehensive results
