Describe 'Step-Version' {
    BeforeAll {
        $script:ModuleToLoad = if ($env:PSPUBLISHMODULE_TEST_MANIFEST_PATH) {
            $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH
        } else {
            Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1'
        }

        Import-Module $script:ModuleToLoad -Force | Out-Null

        $script:StepVersionTestRoot = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ([System.Guid]::NewGuid().ToString('N'))
        New-Item -Path $script:StepVersionTestRoot -ItemType Directory -Force | Out-Null
        $script:StepVersionManifest = Join-Path -Path $script:StepVersionTestRoot -ChildPath 'PowerShellManager.psd1'
        @"
@{
    RootModule = 'PowerShellManager.psm1'
    ModuleVersion = '0.1.2'
    GUID = '00000000-0000-0000-0000-000000000001'
}
"@ | Set-Content -Path $script:StepVersionManifest -Encoding UTF8
    }

    AfterAll {
        if ($script:StepVersionTestRoot -and (Test-Path -LiteralPath $script:StepVersionTestRoot)) {
            Remove-Item -LiteralPath $script:StepVersionTestRoot -Recurse -Force
        }
    }

    It 'Testing version 0.1.X' {
        $Output = Step-Version -Module 'PowerShellManager' -LocalPSD1 $script:StepVersionManifest -ExpectedVersion '0.1.X'
        $Output | Should -Be "0.1.3"
    }

    It 'Testing version 0.2.X' {
        $Output = Step-Version -Module 'PowerShellManager' -LocalPSD1 $script:StepVersionManifest -ExpectedVersion '0.2.X'
        $Output | Should -Be "0.2.0"
    }
}
