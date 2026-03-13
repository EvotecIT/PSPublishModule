Describe 'Step-Version' {
    It 'Testing version 0.1.X' {
        $ModuleToLoad = if ($env:PSPUBLISHMODULE_TEST_MANIFEST_PATH) { $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH } else { Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1' }
        Import-Module $ModuleToLoad -Force | Out-Null
        $Output = Step-Version -Module 'PowerShellManager' -ExpectedVersion '0.1.X'
        $Output | Should -Be "0.1.3"
    }
    It "Testing version 0.2.X" {
        $ModuleToLoad = if ($env:PSPUBLISHMODULE_TEST_MANIFEST_PATH) { $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH } else { Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1' }
        Import-Module $ModuleToLoad -Force | Out-Null
        $Output = Step-Version -Module 'PowerShellManager' -ExpectedVersion '0.2.X'
        $Output | Should -Be "0.2.0"
    }
}
