Describe 'Step-Version' {
    It 'Testing version 0.1.X' {
        $ModuleToLoad = Join-Path -Path $PSScriptRoot -ChildPath '..' -AdditionalChildPath 'PSPublishModule.psd1'
        Import-Module $ModuleToLoad -Force | Out-Null
        $Output = Step-Version -Module 'PowerShellManager' -ExpectedVersion '0.1.X'
        $Output | Should -Be "0.1.3"
    }
    It "Testing version 0.2.X" {
        $ModuleToLoad = Join-Path -Path $PSScriptRoot -ChildPath '..' -AdditionalChildPath 'PSPublishModule.psd1'
        Import-Module $ModuleToLoad -Force | Out-Null
        $Output = Step-Version -Module 'PowerShellManager' -ExpectedVersion '0.2.X'
        $Output | Should -Be "0.2.0"
    }
}
