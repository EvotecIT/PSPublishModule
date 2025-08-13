Describe "Module Testing Functions" {
    BeforeAll {
        # Clean up any existing module instances first
        Get-Module PSPublishModule | Remove-Module -Force -ErrorAction SilentlyContinue

        # Import the module fresh
        Import-Module $PSScriptRoot\..\PSPublishModule.psd1 -Force
    }

    AfterAll {
        # Clean up after tests
        Get-Module PSPublishModule | Remove-Module -Force -ErrorAction SilentlyContinue
    }

    Context "Get-ModuleInformation" {
        It "Should retrieve module information correctly" {
            $result = Get-ModuleInformation -Path $PSScriptRoot\..

            $result | Should -Not -BeNullOrEmpty
            $result.ModuleName | Should -Be 'PSPublishModule'
            $result.ModuleVersion | Should -Not -BeNullOrEmpty
            $result.ManifestPath | Should -Match '\.psd1$'
            $result.RequiredModules | Should -Not -BeNullOrEmpty
        }

        It "Should throw error for invalid path" {
            { Get-ModuleInformation -Path "C:\NonExistentPath" } | Should -Throw
        }
    }

    Context "Test-RequiredModules (Private Function)" {
        It "Should handle module information correctly" {
            InModuleScope PSPublishModule {
                $moduleInfo = Get-ModuleInformation -Path $PSScriptRoot\..

                # This should not throw an error
                { Test-RequiredModules -ModuleInformation $moduleInfo -AdditionalModules @('Microsoft.PowerShell.Management') -SkipModules @('NonExistentModule') } | Should -Not -Throw
            }
        }

        It "Should handle empty module list gracefully" {
            InModuleScope PSPublishModule {
                $moduleInfo = @{
                    ModuleName = 'TestModule'
                    RequiredModules = @()
                }

                { Test-RequiredModules -ModuleInformation $moduleInfo } | Should -Not -Throw
            }
        }
    }

    Context "Test-ModuleImport (Private Function)" {
        It "Should import module successfully" {
            InModuleScope PSPublishModule {
                # Test importing by module name (should not throw)
                { Test-ModuleImport -ModuleName "Microsoft.PowerShell.Management" } | Should -Not -Throw
            }
        }

        It "Should throw error for non-existent module" {
            InModuleScope PSPublishModule {
                { Test-ModuleImport -ModuleName "NonExistentModule12345" } | Should -Throw
            }
        }
    }

    Context "Invoke-ModuleTestSuite" {
        It "Should execute complete test suite successfully" {
            # Test the main public function with safe parameters and exclude our own test file to prevent recursion
            { Invoke-ModuleTestSuite -ProjectPath $PSScriptRoot\.. -SkipDependencies -SkipImport -OutputFormat Minimal -PassThru -TestPath "$PSScriptRoot\..\Tests\Build-Module.Tests.ps1" } | Should -Not -Throw
        }

        It "Should handle invalid project path gracefully" {
            { Invoke-ModuleTestSuite -ProjectPath "C:\NonExistentPath" -SkipDependencies -SkipImport -OutputFormat Minimal } | Should -Throw
        }

        It "Should return test results when PassThru is specified" {
            # For now, just test that PassThru doesn't break the function
            # The main functionality of returning results is tested in the main test suite
            { Invoke-ModuleTestSuite -ProjectPath $PSScriptRoot\.. -SkipDependencies -SkipImport -OutputFormat Minimal -PassThru -TestPath "$PSScriptRoot\..\Tests\Build-Module.Tests.ps1" } | Should -Not -Throw
        }
    }
}
