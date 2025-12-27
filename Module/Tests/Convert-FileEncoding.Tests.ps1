Describe 'Convert-ProjectEncoding' {
    BeforeAll {
        if ($IsWindows) {
            $TempDir = $env:TEMP
        } else {
            $TempDir = '/tmp'
        }
        # Always import the local module from the repository to avoid picking up an installed copy.
        $ModuleToLoad = Join-Path -Path $PSScriptRoot -ChildPath '..' -AdditionalChildPath 'PSPublishModule.psd1'
        Import-Module $ModuleToLoad -Force

        function Get-TestFileEncodingName {
            [CmdletBinding()]
            param(
                [Parameter(Mandatory)]
                [string] $Path
            )

            $bytes = [System.IO.File]::ReadAllBytes($Path)

            if ($bytes.Length -ge 4 -and $bytes[0] -eq 0x00 -and $bytes[1] -eq 0x00 -and $bytes[2] -eq 0xfe -and $bytes[3] -eq 0xff) { return 'UTF32' }
            if ($bytes.Length -ge 4 -and $bytes[0] -eq 0xff -and $bytes[1] -eq 0xfe -and $bytes[2] -eq 0x00 -and $bytes[3] -eq 0x00) { return 'UTF32' }
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf) { return 'UTF8BOM' }
            if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xff -and $bytes[1] -eq 0xfe) { return 'Unicode' }
            if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xfe -and $bytes[1] -eq 0xff) { return 'BigEndianUnicode' }
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 0x2b -and $bytes[1] -eq 0x2f -and $bytes[2] -eq 0x76) { return 'UTF7' }

            foreach ($b in $bytes) { if ($b -gt 0x7F) { return 'UTF8' } }
            return 'ASCII'
        }
    }

    It 'Returns encoding name by default' {
        $f = [System.IO.Path]::GetTempFileName()
        # Use UTF8 content with non-ASCII characters to ensure UTF8 detection
        [System.IO.File]::WriteAllText($f, 'tëst', [System.Text.UTF8Encoding]::new($false))

        $enc = Get-TestFileEncodingName -Path $f
        $enc | Should -Be 'UTF8'
        Remove-Item $f -Force
    }

    It 'Returns ASCII for pure ASCII content' {
        $f = [System.IO.Path]::GetTempFileName()
        # Pure ASCII content should be detected as ASCII
        [System.IO.File]::WriteAllText($f, 'test', [System.Text.UTF8Encoding]::new($false))

        $enc = Get-TestFileEncodingName -Path $f
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

            # Verify initial encodings
            $enc1Before = Get-TestFileEncodingName -Path $File1
            $enc2Before = Get-TestFileEncodingName -Path $File2
            $enc1Before | Should -Be 'UTF8BOM'
            $enc2Before | Should -Be 'ASCII'  # Pure ASCII content

            # Convert using the public function
            Convert-ProjectEncoding -Path $TestDir -ProjectType PowerShell -TargetEncoding UTF8BOM -Force -WhatIf:$false

            # Verify conversions
            $enc1After = Get-TestFileEncodingName -Path $File1
            $enc2After = Get-TestFileEncodingName -Path $File2
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

            # Should be detected as UTF8 due to Unicode characters
            $encBefore = Get-TestFileEncodingName -Path $File
            $encBefore | Should -Be 'UTF8'

            # Convert to UTF8BOM
            Convert-ProjectEncoding -Path $TestDir -ProjectType PowerShell -TargetEncoding UTF8BOM -Force -WhatIf:$false

            # Should now be UTF8BOM
            $encAfter = Get-TestFileEncodingName -Path $File
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
