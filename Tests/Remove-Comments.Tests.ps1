Describe 'Remove-Comments' {
    It 'Save to variable' {
        $FilePath = "$PSScriptRoot\Input\RemoveCommentsTests.ps1"
        $Output = Remove-Comments -SourceFilePath $FilePath
        $LineByLine = $Output -split "`r`n"
        $LineByLine.Count | Should -Be 71
    }
    It 'Save to file' {
        $FilePath = "$PSScriptRoot\Input\RemoveCommentsTests.ps1"
        $OutputFilePath = "$Env:Temp\RemoveCommentsTests1.ps1"
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 71
    }
    It 'Save to file - RemoveAllEmptyLines' {
        $FilePath = "$PSScriptRoot\Input\RemoveCommentsTests.ps1"
        $OutputFilePath = "$Env:Temp\RemoveCommentsTests2.ps1"
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath -RemoveAllEmptyLines
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 45
    }
    It 'Save to file - RemoveEmptyLines' {
        $FilePath = "$PSScriptRoot\Input\RemoveCommentsTests.ps1"
        $OutputFilePath = "$Env:Temp\RemoveCommentsTests3.ps1"
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath -RemoveEmptyLines
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 57
    }

    It 'Save to file - RemoveEmptyLines' {
        $FilePath = "$PSScriptRoot\Input\RemoveCommentsTests.ps1"
        $OutputFilePath = "$Env:Temp\RemoveCommentsTests4.ps1"
        Remove-Item -Path $OutputFilePath -Force -ErrorAction SilentlyContinue
        Remove-Comments -SourceFilePath $FilePath -DestinationFilePath $OutputFilePath -RemoveCommentsInParamBlock -RemoveCommentsBeforeParamBlock -RemoveAllEmptyLines
        $Output = Get-Content -Path $OutputFilePath
        $Output.Count | Should -Be 28
    }
}