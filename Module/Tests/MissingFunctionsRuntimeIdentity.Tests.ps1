BeforeAll {
    $script:moduleDll = (Get-Command Get-MissingFunctions -ErrorAction Stop).ImplementingType.Assembly.Location
    $script:hostPath = (Get-Process -Id $PID).Path
    $script:fixture = Join-Path $PSScriptRoot 'Fixtures/SelectedRuntimeBinaryIdentity.ps1'
}

Describe 'Selected metadata installation roots' {
    It 'keeps the build-entry search path for later import validation' {
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $root
        $fixture = Join-Path $PSScriptRoot 'Fixtures/SelectedRuntimeHostedPath.ps1'
        $output = & $script:hostPath -NoProfile -NonInteractive -File $fixture -ModuleDll $script:moduleDll -Root $root
        $LASTEXITCODE | Should -Be 0
        $output | Should -Contain 'Hosted import passed'
    }

    It 'restores the build search path between required module imports' {
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $root
        $fixture = Join-Path $PSScriptRoot 'Fixtures/SelectedRuntimeImportPath.ps1'
        $output = & $script:hostPath -NoProfile -NonInteractive -File $fixture -ModuleDll $script:moduleDll -Root $root
        $LASTEXITCODE | Should -Be 0
        $output | Should -Not -Contain 'PFIMPORT::FAILED'
    }

    It 'uses the selected root only when requested: <PreferSelectedRoot>' -ForEach @(
        @{ PreferSelectedRoot = 0; ExpectedVersion = '2.0.0' }
        @{ PreferSelectedRoot = 1; ExpectedVersion = '1.0.0' }
    ) {
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $root
        $fixture = Join-Path $PSScriptRoot 'Fixtures/SelectedRuntimeMetadataRoot.ps1'
        $version = & $script:hostPath -NoProfile -NonInteractive -File $fixture -ModuleDll $script:moduleDll -Root $root -PreferSelectedRoot $PreferSelectedRoot
        $LASTEXITCODE | Should -Be 0
        $version | Should -Be $ExpectedVersion
    }
}

Describe 'Selected manifest identity with a same-name nested binary' {
    It 'validates the wrapper manifest while resolving its binary command: <Case>' -ForEach @(
        @{ Case = 'matching-manifest'; ActualVersion = '9.8.7'; WrongGuid = $false; ExpectedError = $null }
        @{ Case = 'wrong-manifest-guid'; ActualVersion = '9.8.7'; WrongGuid = $true; ExpectedError = '*GUID*does not match*' }
        @{ Case = 'wrong-manifest-version'; ActualVersion = '9.8.6'; WrongGuid = $false; ExpectedError = '*version*does not match*' }
        @{ Case = 'sibling-transitive-dependency'; ActualVersion = '9.8.7'; WrongGuid = $false; ExpectedError = $null; WithDependency = $true }
    ) {
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $root
        $expectedGuid = [guid]::NewGuid().ToString()
        $actualGuid = if ($WrongGuid) { [guid]::NewGuid().ToString() } else { $expectedGuid }
        # The child owns loaded binary handles; they are released before TestDrive cleanup.
        $json = & $script:hostPath -NoProfile -NonInteractive -File $script:fixture -ModuleDll $script:moduleDll -Root $root -ActualVersion $ActualVersion -ExpectedGuid $expectedGuid -ActualGuid $actualGuid -WithDependency ([int] [bool] $WithDependency)
        $LASTEXITCODE | Should -Be 0
        $result = $json | ConvertFrom-Json
        $result.SummaryCount | Should -Be 1
        $result.ModulePathUnchanged | Should -BeTrue
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
