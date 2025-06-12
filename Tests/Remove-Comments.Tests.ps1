Describe 'Remove-Comments' {
    BeforeAll {
        # Set up temp directory
        if ($IsWindows) {
            $TempDir = $env:TEMP
        } else {
            $TempDir = '/tmp'
        }
    }

    It 'Save to variable' {
        $FilePath = [io.path]::Combine($PSScriptRoot, 'Input', 'RemoveCommentsTests.ps1')
        $Output = Remove-Comments -SourceFilePath $FilePath
        $LineByLine = $Output -split "`n"
        $LineByLine.Count | Should -Be 71
    }

    It 'Save to file' {
        $FilePath = [io.path]::Combine($PSScriptRoot, 'Input', 'RemoveCommentsTests.ps1')
        $OutputFilePath = [io.path]::Combine($TempDir, 'RemoveCommentsTests1.ps1')
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 71
    }

    It 'Save to file - RemoveAllEmptyLines' {
        $FilePath = [io.path]::Combine($PSScriptRoot, 'Input', 'RemoveCommentsTests.ps1')
        $OutputFilePath = [io.path]::Combine($TempDir, 'RemoveCommentsTests2.ps1')
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath -RemoveAllEmptyLines
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 45
    }

    It 'Save to file - RemoveEmptyLines' {
        $FilePath = [io.path]::Combine($PSScriptRoot, 'Input', 'RemoveCommentsTests.ps1')
        $OutputFilePath = [io.path]::Combine($TempDir, 'RemoveCommentsTests3.ps1')
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath -RemoveEmptyLines
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 57
    }

    It 'Save to file - RemoveEmptyLines with all options' {
        $FilePath = [io.path]::Combine($PSScriptRoot, 'Input', 'RemoveCommentsTests.ps1')
        $OutputFilePath = [io.path]::Combine($TempDir, 'RemoveCommentsTests4.ps1')
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath -RemoveCommentsInParamBlock -RemoveCommentsBeforeParamBlock -RemoveAllEmptyLines
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 28
    }
}