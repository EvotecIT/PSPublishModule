BeforeAll {
    $script:moduleDll = (Get-Command Get-MissingFunctions -ErrorAction Stop).ImplementingType.Assembly.Location
    $script:hostPath = (Get-Process -Id $PID).Path
    $script:fixture = Join-Path $PSScriptRoot 'Fixtures/SelectedRuntimeBinaryIdentity.ps1'
}

Describe 'Selected manifest identity with a same-name nested binary' {
    It 'validates the wrapper manifest while resolving its binary command: <Case>' -ForEach @(
        @{ Case = 'matching-manifest'; ActualVersion = '9.8.7'; WrongGuid = $false; ExpectedError = $null }
        @{ Case = 'wrong-manifest-guid'; ActualVersion = '9.8.7'; WrongGuid = $true; ExpectedError = '*GUID*does not match*' }
        @{ Case = 'wrong-manifest-version'; ActualVersion = '9.8.6'; WrongGuid = $false; ExpectedError = '*version*does not match*' }
    ) {
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $root
        $expectedGuid = [guid]::NewGuid().ToString()
        $actualGuid = if ($WrongGuid) { [guid]::NewGuid().ToString() } else { $expectedGuid }
        # The child owns loaded binary handles; they are released before TestDrive cleanup.
        $json = & $script:hostPath -NoProfile -NonInteractive -File $script:fixture -ModuleDll $script:moduleDll -Root $root -ActualVersion $ActualVersion -ExpectedGuid $expectedGuid -ActualGuid $actualGuid
        $LASTEXITCODE | Should -Be 0
        $result = $json | ConvertFrom-Json
        $result.SummaryCount | Should -Be 1
        if ($ExpectedError) {
            $result.Error | Should -BeLike $ExpectedError
            $result.CommandType | Should -BeNullOrEmpty
        } else {
            $result.Error | Should -BeNullOrEmpty
            $result.Source | Should -Be $result.ModuleName
            $result.CommandType | Should -Be 'Cmdlet'
        }
        $result.FunctionCount | Should -Be 0
    }
}
