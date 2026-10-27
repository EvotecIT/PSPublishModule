BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../ModuleTestSample.psd1') -Force -ErrorAction Stop
}

Describe 'Module workflow sample' {
    It 'imports and runs the module on this test host' {
        Get-ModuleTestGreeting -Name 'Pester' | Should -Be 'Hello, Pester!'
    }

    It 'loads a manifest dependency installed on the other host module path' {
        Get-ModuleTestSampleDependencyValue | Should -Be 'Dependency visible'
    }

    It 'loads a native host module after extending PSModulePath' {
        Get-Command Get-Item -Module Microsoft.PowerShell.Management | Should -Not -BeNullOrEmpty
    }

    It 'runs the exact pinned Pester version' {
        (Get-Module Pester).Version.ToString() | Should -Be $env:MODULE_PESTER_VERSION
    }

    It 'uses the selected shell without a fallback' {
        $expectedEdition = if ($env:MODULE_TEST_SHELL -eq 'powershell') { 'Desktop' } else { 'Core' }
        $PSVersionTable.PSEdition | Should -Be $expectedEdition
    }
}
