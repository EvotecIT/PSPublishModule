function ConvertTo-BenchmarkManifestLiteral {
    param([string] $Value)

    "'" + ($Value -replace "'", "''") + "'"
}

function New-ManagedModulePublishBenchmarkFixture {
    param(
        [string] $Root,
        [string] $Name,
        [string] $Version
    )

    $moduleName = if ([string]::IsNullOrWhiteSpace($Name)) { 'Company.ManagedPublishBenchmark' } else { $Name.Trim() }
    $moduleVersion = if ([string]::IsNullOrWhiteSpace($Version)) { '1.0.0' } else { $Version.Trim() }
    $moduleRoot = Join-Path $Root $moduleName
    New-Item -Path $moduleRoot -ItemType Directory -Force | Out-Null

    $functionName = 'Get-' + (($moduleName -replace '[^A-Za-z0-9]', '') -replace '^$', 'ManagedPublishBenchmark')
    $psm1Path = Join-Path $moduleRoot ($moduleName + '.psm1')
    $psd1Path = Join-Path $moduleRoot ($moduleName + '.psd1')
    Set-Content -LiteralPath $psm1Path -Encoding UTF8 -Value @"
function $functionName {
    'ok'
}
"@

    $nameLiteral = ConvertTo-BenchmarkManifestLiteral -Value $moduleName
    $rootLiteral = ConvertTo-BenchmarkManifestLiteral -Value ($moduleName + '.psm1')
    $functionLiteral = ConvertTo-BenchmarkManifestLiteral -Value $functionName
    Set-Content -LiteralPath $psd1Path -Encoding UTF8 -Value @"
@{
    RootModule = $rootLiteral
    ModuleVersion = '$moduleVersion'
    GUID = '$([guid]::NewGuid().ToString())'
    Author = 'Evotec'
    CompanyName = 'Evotec'
    Copyright = '(c) Evotec. All rights reserved.'
    Description = 'Managed module publish benchmark fixture for $moduleName.'
    FunctionsToExport = @($functionLiteral)
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('benchmark', 'managed-module')
            ProjectUri = 'https://evotec.xyz'
        }
    }
}
"@

    [pscustomobject]@{
        Name = $moduleName
        Version = $moduleVersion
        ModuleRoot = $moduleRoot
        ManifestPath = $psd1Path
    }
}

function New-ManagedModulePublishBenchmarkRepositoryName {
    param(
        [string] $EngineName,
        [int] $Iteration
    )

    'PFMM{0}{1}{2}' -f ($EngineName -replace '[^A-Za-z0-9]', ''), $Iteration, ([guid]::NewGuid().ToString('N').Substring(0, 8))
}

function Register-ManagedModulePublishBenchmarkRepository {
    param(
        [string] $EngineName,
        [string] $Name,
        [string] $Path
    )

    switch ($EngineName) {
        'PSResourceGet' {
            Unregister-PSResourceRepository -Name $Name -ErrorAction SilentlyContinue
            Register-PSResourceRepository -Name $Name -Uri $Path -Trusted -Force -ErrorAction Stop | Out-Null
        }
        'PowerShellGet' {
            Unregister-PSRepository -Name $Name -ErrorAction SilentlyContinue
            Register-PSRepository -Name $Name -SourceLocation $Path -PublishLocation $Path -InstallationPolicy Trusted -ErrorAction Stop | Out-Null
        }
    }
}

function Unregister-ManagedModulePublishBenchmarkRepository {
    param(
        [string] $EngineName,
        [string] $Name
    )

    switch ($EngineName) {
        'PSResourceGet' {
            Unregister-PSResourceRepository -Name $Name -ErrorAction SilentlyContinue
        }
        'PowerShellGet' {
            Unregister-PSRepository -Name $Name -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-ManagedModulePublishBenchmarkCommand {
    param(
        [string] $EngineName,
        [object] $Fixture,
        [string] $RepositoryName,
        [string] $FeedRoot,
        [string] $StageRoot
    )

    switch ($EngineName) {
        'Managed' {
            Publish-ManagedModule -Path $Fixture.ModuleRoot -Repository $FeedRoot -RepositoryName $RepositoryName -OutputDirectory $StageRoot -Force -SkipDependenciesCheck -SkipModuleManifestValidate
        }
        'PSResourceGet' {
            Publish-PSResource -Path $Fixture.ModuleRoot -Repository $RepositoryName -ApiKey 'benchmark' -SkipDependenciesCheck -SkipModuleManifestValidate -ErrorAction Stop | Out-Null
            [pscustomobject]@{
                Name = $Fixture.Name
                Version = $Fixture.Version
            }
        }
        'PowerShellGet' {
            Publish-Module -Path $Fixture.ModuleRoot -Repository $RepositoryName -NuGetApiKey 'benchmark' -Force -ErrorAction Stop | Out-Null
            [pscustomobject]@{
                Name = $Fixture.Name
                Version = $Fixture.Version
            }
        }
    }
}

function Invoke-PublishScenario {
    param(
        [string] $EngineName,
        [int] $Iteration,
        [string] $OperationName = 'Publish'
    )

    switch ($EngineName) {
        'ModuleFast' {
            return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast does not expose an equivalent publish command.'
        }
        'PSResourceGet' {
            if (-not (Test-CommandAvailable 'Publish-PSResource')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Publish-PSResource is not available.'
            }
            if (-not (Test-CommandAvailable 'Register-PSResourceRepository')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Register-PSResourceRepository is not available.'
            }
        }
        'PowerShellGet' {
            if (-not (Test-CommandAvailable 'Publish-Module')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Publish-Module is not available.'
            }
            if (-not (Test-CommandAvailable 'Register-PSRepository')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Register-PSRepository is not available.'
            }
        }
    }

    $operationRoot = Join-Path $publishWorkRoot ("publish-{0}-{1}-{2}" -f $OperationName, $EngineName, $Iteration)
    $sourceRoot = Join-Path $operationRoot 'Source'
    $feedRoot = Join-Path $operationRoot 'Feed'
    $stageRoot = Join-Path $operationRoot 'Stage'
    New-Item -Path $sourceRoot, $feedRoot, $stageRoot -ItemType Directory -Force | Out-Null
    $fixture = New-ManagedModulePublishBenchmarkFixture -Root $sourceRoot -Name $ModuleName -Version $Version
    $publishRepositoryName = New-ManagedModulePublishBenchmarkRepositoryName -EngineName $EngineName -Iteration $Iteration

    try {
        Register-ManagedModulePublishBenchmarkRepository -EngineName $EngineName -Name $publishRepositoryName -Path $feedRoot
    } catch {
        return New-FailedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason "Publish repository registration failed: $($_.Exception.Message)" -OutputRoot $operationRoot
    }

    try {
        Invoke-TimedOperation -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -OutputRoot $operationRoot -DetailPath '' -ScriptBlock {
            Invoke-ManagedModulePublishBenchmarkCommand -EngineName $EngineName -Fixture $fixture -RepositoryName $publishRepositoryName -FeedRoot $feedRoot -StageRoot $stageRoot
        }
    } finally {
        Unregister-ManagedModulePublishBenchmarkRepository -EngineName $EngineName -Name $publishRepositoryName
    }
}
