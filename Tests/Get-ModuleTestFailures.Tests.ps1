Describe "Get-ModuleTestFailures Tests" {
    BeforeAll {
        # Import the module
        Import-Module PSPublishModule -Force
    }

    Context "Parameter Validation" {
        It "Should accept TestResults parameter" {
            # Mock test results object
            $mockTestResults = [PSCustomObject]@{
                TotalCount = 5
                PassedCount = 3
                FailedCount = 2
                Tests = @(
                    [PSCustomObject]@{ Name = "Test1"; Result = "Passed" }
                    [PSCustomObject]@{ Name = "Test2"; Result = "Failed"; ErrorRecord = [PSCustomObject]@{ Exception = [PSCustomObject]@{ Message = "Mock error" } } }
                )
            }

            { Get-ModuleTestFailures -TestResults $mockTestResults -PassThru } | Should -Not -Throw
        }

        It "Should validate OutputFormat parameter" {
            { Get-ModuleTestFailures -OutputFormat "InvalidFormat" } | Should -Throw
        }

        It "Should accept valid OutputFormat values" {
            $validFormats = @('Summary', 'Detailed', 'JSON')
            foreach ($format in $validFormats) {
                { Get-ModuleTestFailures -TestResults @{} -OutputFormat $format -PassThru } | Should -Not -Throw
            }
        }
    }

    Context "TestResults Processing" {
        It "Should process Pester v5 format results" {
            $mockTestResults = [PSCustomObject]@{
                TotalCount = 3
                PassedCount = 2
                FailedCount = 1
                SkippedCount = 0
                Tests = @(
                    [PSCustomObject]@{
                        Name = "Passing Test"
                        Result = "Passed"
                    }
                    [PSCustomObject]@{
                        Name = "Failing Test"
                        Result = "Failed"
                        ErrorRecord = [PSCustomObject]@{
                            Exception = [PSCustomObject]@{ Message = "Expected 'actual' to be 'expected'" }
                            ScriptStackTrace = "at line 10"
                        }
                        Time = [TimeSpan]::FromSeconds(0.5)
                    }
                )
            }

            $result = Get-ModuleTestFailures -TestResults $mockTestResults -PassThru

            $result.TotalCount | Should -Be 3
            $result.PassedCount | Should -Be 2
            $result.FailedCount | Should -Be 1
            $result.FailedTests.Count | Should -Be 1
            $result.FailedTests[0].Name | Should -Be "Failing Test"
            $result.FailedTests[0].ErrorMessage | Should -Be "Expected 'actual' to be 'expected'"
        }

        It "Should process Pester v4 format results" {
            $mockTestResults = [PSCustomObject]@{
                TotalCount = 2
                PassedCount = 1
                FailedCount = 1
                TestResult = @(
                    [PSCustomObject]@{
                        Name = "V4 Passing Test"
                        Result = "Passed"
                    }
                    [PSCustomObject]@{
                        Describe = "TestSuite"
                        Context = "TestContext"
                        It = "V4 Failing Test"
                        Result = "Failed"
                        FailureMessage = "V4 style failure message"
                        Time = [TimeSpan]::FromSeconds(1.2)
                    }
                )
            }

            $result = Get-ModuleTestFailures -TestResults $mockTestResults -PassThru

            $result.TotalCount | Should -Be 2
            $result.FailedCount | Should -Be 1
            $result.FailedTests[0].Name | Should -Be "TestSuite TestContext V4 Failing Test"
            $result.FailedTests[0].ErrorMessage | Should -Be "V4 style failure message"
        }
    }

    Context "Output Formats" {
        BeforeAll {
            $mockTestResults = [PSCustomObject]@{
                TotalCount = 2
                PassedCount = 1
                FailedCount = 1
                Tests = @(
                    [PSCustomObject]@{
                        Name = "Test Failure"
                        Result = "Failed"
                        ErrorRecord = [PSCustomObject]@{ Exception = [PSCustomObject]@{ Message = "Test error" } }
                    }
                )
            }
        }

        It "Should generate JSON output" {
            $jsonOutput = Get-ModuleTestFailures -TestResults $mockTestResults -OutputFormat JSON
            $jsonOutput | Should -Not -BeNullOrEmpty

            # Verify it's valid JSON
            $parsed = $jsonOutput | ConvertFrom-Json
            $parsed.TotalCount | Should -Be 2
            $parsed.FailedCount | Should -Be 1
        }

        It "Should return analysis object with PassThru" {
            $result = Get-ModuleTestFailures -TestResults $mockTestResults -PassThru

            $result | Should -BeOfType [PSCustomObject]
            $result.Source | Should -Be 'PesterResults'
            $result.Timestamp | Should -BeOfType [DateTime]
        }
    }

    Context "File Processing" {
        It "Should handle missing test results file gracefully" {
            $nonExistentPath = "C:\NonExistent\TestResults.xml"
            { Get-ModuleTestFailures -Path $nonExistentPath } | Should -Not -Throw
        }

        It "Should search in standard locations when no path specified" {
            # This test verifies the function doesn't throw when searching for files
            { Get-ModuleTestFailures -ProjectPath $TestDrive } | Should -Not -Throw
        }
    }

    Context "Edge Cases" {
        It "Should handle empty test results" {
            $emptyResults = [PSCustomObject]@{
                TotalCount = 0
                PassedCount = 0
                FailedCount = 0
                Tests = @()
            }

            $result = Get-ModuleTestFailures -TestResults $emptyResults -PassThru
            $result.TotalCount | Should -Be 0
            $result.FailedTests.Count | Should -Be 0
        }

        It "Should handle results with no error messages" {
            $resultsWithoutErrors = [PSCustomObject]@{
                TotalCount = 1
                PassedCount = 0
                FailedCount = 1
                Tests = @(
                    [PSCustomObject]@{
                        Name = "Test Without Error"
                        Result = "Failed"
                    }
                )
            }

            $result = Get-ModuleTestFailures -TestResults $resultsWithoutErrors -PassThru
            $result.FailedTests[0].ErrorMessage | Should -Be 'No error message available'
        }
    }
}