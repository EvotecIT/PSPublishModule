Describe 'Convert-ProjectEncoding' {
    BeforeAll {
        if ($IsWindows) {
            $TempDir = $env:TEMP
        } else {
            $TempDir = '/tmp'
        }

        # Import the module to ensure functions are available
        Import-Module "$PSScriptRoot\..\PSPublishModule.psd1" -Force
    }

    It 'Returns encoding name by default' {
        $f = [System.IO.Path]::GetTempFileName()
        # Use UTF8 content with non-ASCII characters to ensure UTF8 detection
        [System.IO.File]::WriteAllText($f, 'tëst', [System.Text.UTF8Encoding]::new($false))

        # Import module with PassThru to access private functions
        $Module = Import-Module "$PSScriptRoot\..\PSPublishModule.psd1" -Force -PassThru
        $enc = & $Module Get-FileEncoding -Path $f
        $enc | Should -Be 'UTF8'
        Remove-Item $f -Force
    }

    It 'Returns ASCII for pure ASCII content' {
        $f = [System.IO.Path]::GetTempFileName()
        # Pure ASCII content should be detected as ASCII
        [System.IO.File]::WriteAllText($f, 'test', [System.Text.UTF8Encoding]::new($false))

        # Import module with PassThru to access private functions
        $Module = Import-Module "$PSScriptRoot\..\PSPublishModule.psd1" -Force -PassThru
        $enc = & $Module Get-FileEncoding -Path $f
        $enc | Should -Be 'ASCII'
        Remove-Item $f -Force
    }

    It 'Converts files using Convert-ProjectEncoding' {
        # Create a temporary directory with test files
        $TestDir = Join-Path $TempDir 'encoding-test'
        if (Test-Path $TestDir) { Remove-Item $TestDir -Recurse -Force }
        New-Item -Path $TestDir -ItemType Directory | Out-Null

        try {
            # Create test files with different encodings
            $File1 = Join-Path $TestDir 'test1.ps1'
            $File2 = Join-Path $TestDir 'test2.ps1'

            [System.IO.File]::WriteAllText($File1, 'Write-Host "Hello"', [System.Text.UTF8Encoding]::new($true))  # UTF8BOM
            [System.IO.File]::WriteAllText($File2, 'Write-Host "World"', [System.Text.UTF8Encoding]::new($false)) # UTF8

            # Import module with PassThru to access private functions for verification
            $Module = Import-Module "$PSScriptRoot\..\PSPublishModule.psd1" -Force -PassThru

            # Verify initial encodings
            $enc1Before = & $Module Get-FileEncoding -Path $File1
            $enc2Before = & $Module Get-FileEncoding -Path $File2
            $enc1Before | Should -Be 'UTF8BOM'
            $enc2Before | Should -Be 'ASCII'  # Pure ASCII content

            # Convert using the public function
            Convert-ProjectEncoding -Path $TestDir -ProjectType PowerShell -TargetEncoding UTF8BOM -Force -WhatIf:$false

            # Verify conversions
            $enc1After = & $Module Get-FileEncoding -Path $File1
            $enc2After = & $Module Get-FileEncoding -Path $File2
            $enc1After | Should -Be 'UTF8BOM'  # Should remain UTF8BOM
            $enc2After | Should -Be 'UTF8BOM'  # Should be converted to UTF8BOM

        } finally {
            if (Test-Path $TestDir) { Remove-Item $TestDir -Recurse -Force }
        }
    }

    It 'Handles mixed content correctly' {
        $TestDir = Join-Path $TempDir 'encoding-test-mixed'
        if (Test-Path $TestDir) { Remove-Item $TestDir -Recurse -Force }
        New-Item -Path $TestDir -ItemType Directory | Out-Null

        try {
            $File = Join-Path $TestDir 'unicode-test.ps1'
            $text = 'Write-Host "Zażółć gęślą jaźń"'  # Polish text with Unicode characters
            [System.IO.File]::WriteAllText($File, $text, [System.Text.UTF8Encoding]::new($false))

            # Import module with PassThru to access private functions for verification
            $Module = Import-Module "$PSScriptRoot\..\PSPublishModule.psd1" -Force -PassThru

            # Should be detected as UTF8 due to Unicode characters
            $encBefore = & $Module Get-FileEncoding -Path $File
            $encBefore | Should -Be 'UTF8'

            # Convert to UTF8BOM
            Convert-ProjectEncoding -Path $TestDir -ProjectType PowerShell -TargetEncoding UTF8BOM -Force -WhatIf:$false

            # Should now be UTF8BOM
            $encAfter = & $Module Get-FileEncoding -Path $File
            $encAfter | Should -Be 'UTF8BOM'

            # Content should remain unchanged
            $content = Get-Content -LiteralPath $File -Raw -Encoding UTF8
            $content.Trim() | Should -Be $text

        } finally {
            if (Test-Path $TestDir) { Remove-Item $TestDir -Recurse -Force }
        }
    }

    It 'Skips files when encoding does not match and Force is not used' {
        $TestDir = Join-Path $TempDir 'encoding-test-skip'
        if (Test-Path $TestDir) { Remove-Item $TestDir -Recurse -Force }
        New-Item -Path $TestDir -ItemType Directory | Out-Null

        try {
            $File = Join-Path $TestDir 'test.ps1'
            [System.IO.File]::WriteAllText($File, 'Write-Host "Test"', [System.Text.UTF8Encoding]::new($false))
            $beforeBytes = [System.IO.File]::ReadAllBytes($File)

            # Try to convert without Force - should skip
            Convert-ProjectEncoding -Path $TestDir -ProjectType PowerShell -SourceEncoding UTF8BOM -TargetEncoding UTF8 -WhatIf:$false

            $afterBytes = [System.IO.File]::ReadAllBytes($File)
            $afterBytes | Should -Be $beforeBytes

        } finally {
            if (Test-Path $TestDir) { Remove-Item $TestDir -Recurse -Force }
        }
    }
}
