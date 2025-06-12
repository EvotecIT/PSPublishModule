Describe 'Remove-Comments' {
    It 'Save to variable' {
        $FilePath = Join-Path -Path $PSScriptRoot -ChildPath 'Input' -AdditionalChildPath 'RemoveCommentsTests.ps1'
        $Output = Remove-Comments -SourceFilePath $FilePath
        $LineByLine = $Output -split "`n"
        $LineByLine.Count | Should -Be 71
    }
    It 'Save to file' {
        $FilePath = Join-Path -Path $PSScriptRoot -ChildPath 'Input' -AdditionalChildPath 'RemoveCommentsTests.ps1'
        $OutputFilePath = Join-Path -Path $env:TEMP -ChildPath 'RemoveCommentsTests1.ps1'
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 71
    }
    It 'Save to file - RemoveAllEmptyLines' {
        $FilePath = Join-Path -Path $PSScriptRoot -ChildPath 'Input' -AdditionalChildPath 'RemoveCommentsTests.ps1'
        $OutputFilePath = Join-Path -Path $env:TEMP -ChildPath 'RemoveCommentsTests2.ps1'
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath -RemoveAllEmptyLines
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 45
    }
    It 'Save to file - RemoveEmptyLines' {
        $FilePath = Join-Path -Path $PSScriptRoot -ChildPath 'Input' -AdditionalChildPath 'RemoveCommentsTests.ps1'
        $OutputFilePath = Join-Path -Path $env:TEMP -ChildPath 'RemoveCommentsTests3.ps1'
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath -RemoveEmptyLines
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 57
    }

    It 'Save to file - RemoveEmptyLines' {
        $FilePath = Join-Path -Path $PSScriptRoot -ChildPath 'Input' -AdditionalChildPath 'RemoveCommentsTests.ps1'
        $OutputFilePath = Join-Path -Path $env:TEMP -ChildPath 'RemoveCommentsTests4.ps1'
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath -RemoveCommentsInParamBlock -RemoveCommentsBeforeParamBlock -RemoveAllEmptyLines
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 28
    }
}