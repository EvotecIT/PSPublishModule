Describe 'Step-Version' {
    It 'Testing version 0.1.X' {
        if (-not (Get-Module -ListAvailable -Name 'PSPublishModule')) {
            $ModuleToLoad = "$PSScriptRoot\..\PSPublishModule\PSPublishModule.psd1"
        } else {
            $ModuleToLoad = 'PSPublishModule'
        }
        $Features = Import-Module $ModuleToLoad -PassThru
        $Output = & $Features {
            Step-Version -Module 'PowerShellManager' -ExpectedVersion '0.1.X'
        }
        $Output | Should -Be "0.1.3"
    }
    It "Testing version 0.2.X" {
        if (-not (Get-Module -ListAvailable -Name 'PSPublishModule')) {
            $ModuleToLoad = "$PSScriptRoot\..\PSPublishModule\PSPublishModule.psd1"
        } else {
            $ModuleToLoad = 'PSPublishModule'
        }
        $Features = Import-Module $ModuleToLoad -PassThru
        $Output = & $Features {
            Step-Version -Module 'PowerShellManager' -ExpectedVersion '0.2.X'
        }
        $Output | Should -Be "0.2.0"
    }
}