Describe 'Step-Version' {
    BeforeAll {
        $script:ModuleToLoad = if ($env:PSPUBLISHMODULE_TEST_MANIFEST_PATH) {
            $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH
        } else {
            Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1'
        }

        Import-Module $script:ModuleToLoad -Force | Out-Null
    }

    It 'Testing version 0.1.X' {
        $Output = Step-Version -Module 'PowerShellManager' -ExpectedVersion '0.1.X'
        $Output | Should -Be "0.1.3"
    }

    It 'Testing version 0.2.X' {
        $Output = Step-Version -Module 'PowerShellManager' -ExpectedVersion '0.2.X'
        $Output | Should -Be "0.2.0"
    }
}
