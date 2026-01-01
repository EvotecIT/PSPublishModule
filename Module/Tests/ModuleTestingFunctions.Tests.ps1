Describe "Module Testing Functions" {
    BeforeAll {
        # Clean up any existing module instances first
        Get-Module PSPublishModule | Remove-Module -Force -ErrorAction SilentlyContinue

        # Import the module fresh
        $moduleManifest = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1'
        Import-Module $moduleManifest -Force
    }

    AfterAll {
        # Clean up after tests
        Get-Module PSPublishModule | Remove-Module -Force -ErrorAction SilentlyContinue
    }

    Context "Get-ModuleInformation" {
        It "Should retrieve module information correctly" {
            $result = Get-ModuleInformation -Path (Join-Path -Path $PSScriptRoot -ChildPath '..')

            $result | Should -Not -BeNullOrEmpty
            $result.ModuleName | Should -Be 'PSPublishModule'
            $result.ModuleVersion | Should -Not -BeNullOrEmpty
            $result.ManifestPath | Should -Match '\.psd1$'
            $result.RequiredModules | Should -Not -BeNullOrEmpty
        }

        It "Should throw error for invalid path" {
            $invalidPath = Join-Path -Path $TestDrive -ChildPath 'NonExistentPath'
            { Get-ModuleInformation -Path $invalidPath } | Should -Throw
        }
    }

    Context "Invoke-ModuleTestSuite" {
        BeforeAll {
            $script:moduleTestSuiteResult = $null
            $script:moduleTestSuiteException = $null

            $invokeParams = @{
                ProjectPath      = (Join-Path -Path $PSScriptRoot -ChildPath '..')
                SkipDependencies = $true
                SkipImport       = $true
                OutputFormat     = 'Minimal'
                PassThru         = $true
                TimeoutSeconds   = 600
                TestPath         = [IO.Path]::Combine($PSScriptRoot, '..', 'Tests', 'Build-Module.Tests.ps1')
            }

            try {
                $script:moduleTestSuiteResult = Invoke-ModuleTestSuite @invokeParams
            } catch {
                $script:moduleTestSuiteException = $_
            }
        }

        It "Should execute complete test suite successfully" {
            $script:moduleTestSuiteException | Should -BeNullOrEmpty
        }

        It "Should handle invalid project path gracefully" {
            $invalidProjectPath = Join-Path -Path $TestDrive -ChildPath 'NonExistentProjectPath'
            { Invoke-ModuleTestSuite -ProjectPath $invalidProjectPath -SkipDependencies -SkipImport -OutputFormat Minimal } | Should -Throw
        }

        It "Should return test results when PassThru is specified" {
            $script:moduleTestSuiteResult | Should -Not -BeNullOrEmpty
        }
    }
}
