Describe 'Convert-FileEncoding' {
    BeforeAll {
        if ($IsWindows) {
            $TempDir = $env:TEMP
        } else {
            $TempDir = '/tmp'
        }

        $ModuleRoot = Join-Path $PSScriptRoot '..'
        . (Join-Path $ModuleRoot 'Private/Get-Encoding.ps1')
        . (Join-Path $ModuleRoot 'Private/Resolve-Encoding.ps1')
        . (Join-Path $ModuleRoot 'Private/Convert-FileEncodingSingle.ps1')
        . (Join-Path $ModuleRoot 'Public/Convert-FileEncoding.ps1')
    }

    It 'Converts UTF8BOM to UTF8 without altering content' {
        $File = Join-Path $TempDir 'convert-test1.txt'
        [System.IO.File]::WriteAllText($File, 'Hello World', [System.Text.UTF8Encoding]::new($true))
        Convert-FileEncoding -Path $File -SourceEncoding UTF8BOM -TargetEncoding UTF8
        $encObj = Get-Encoding -Path $File
        $encObj.Encoding.WebName | Should -Be 'utf-8'
        $encObj.Encoding.GetPreamble().Length | Should -Be 0
        $content = Get-Content -LiteralPath $File -Raw -Encoding UTF8
        $content | Should -Be 'Hello World'
    }

    It 'Rolls back when conversion would change content' {
        $File = Join-Path $TempDir 'convert-test2.txt'
        $text = 'Zażółć gęślą jaźń'
        [System.IO.File]::WriteAllText($File, $text, [System.Text.UTF8Encoding]::new($true))
        Convert-FileEncoding -Path $File -SourceEncoding UTF8BOM -TargetEncoding ASCII
        $encObj = Get-Encoding -Path $File
        $encObj.Encoding.WebName | Should -Be 'utf-8'
        $encObj.Encoding.GetPreamble().Length | Should -Be 3
        $content = Get-Content -LiteralPath $File -Raw -Encoding UTF8
        $content | Should -Be $text
    }

    It 'Skips file when encoding does not match and Force is not used' {
        $File = Join-Path $TempDir 'convert-test3.txt'
        [System.IO.File]::WriteAllText($File, 'Another Test', [System.Text.UTF8Encoding]::new($false))
        $beforeBytes = [System.IO.File]::ReadAllBytes($File)
        Convert-FileEncoding -Path $File -SourceEncoding UTF8BOM -TargetEncoding UTF8
        $afterBytes = [System.IO.File]::ReadAllBytes($File)
        $afterBytes | Should -Be $beforeBytes
    }

    It 'Skips conversion when file already has target encoding' {
        $File = Join-Path $TempDir 'convert-test4.txt'
        [System.IO.File]::WriteAllText($File, 'Final Test', [System.Text.UTF8Encoding]::new($false))
        $beforeBytes = [System.IO.File]::ReadAllBytes($File)
        Convert-FileEncoding -Path $File -SourceEncoding UTF8 -TargetEncoding UTF8
        $afterBytes = [System.IO.File]::ReadAllBytes($File)
        $afterBytes | Should -Be $beforeBytes
    }
}
