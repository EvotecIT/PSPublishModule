function Initialize-InstallerObservation {
    param([string] $ScenarioRoot, [string] $Instance)
    $root = [IO.Path]::GetFullPath($ScenarioRoot)
    $ancestor = Get-Item -LiteralPath $root
    while ($null -ne $ancestor) {
        if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Observer roots cannot traverse links.' }
        $ancestor = $ancestor.Parent
    }
    $state = Get-Content -LiteralPath (Join-Path $root 'installer-fixture/state.json') -Raw | ConvertFrom-Json
    $identity = $state.fixtureIdentity
    if ($identity.address -cne '127.0.0.1' -or $identity.origin -cne ('http://127.0.0.1:' + $identity.port)) { throw 'The fixture is not owned loopback transport.' }
    $process = Get-Process -Id $identity.pid -ErrorAction Stop
    if ($process.StartTime.ToUniversalTime() -ne ([datetime] $identity.startUtc).ToUniversalTime()) { throw 'The fixture process identity changed.' }
    $run = [IO.Path]::GetFullPath((Join-Path $root ('surfaces/' + $Instance)))
    if (-not $run.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Observer directory escapes its scenario root.' }
    $path = $run
    while ($path) {
        if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Observer directories cannot traverse links.'
        }
        $path = [IO.Path]::GetDirectoryName($path)
    }
    $owner = ([datetime] $identity.startUtc).ToUniversalTime().ToString('o') + '|' + $Instance
    if (Test-Path -LiteralPath $run) {
        if ([IO.File]::ReadAllText((Join-Path $run '.qualification-owned')) -cne $owner) { throw 'Existing observer directory has a different owner.' }
        $pending = [Collections.Generic.Stack[IO.FileSystemInfo]]::new()
        $pending.Push((Get-Item -LiteralPath $run))
        while ($pending.Count) {
            $item = $pending.Pop()
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Observer cleanup refuses links.' }
            if ($item -is [IO.DirectoryInfo]) { foreach ($child in $item.GetFileSystemInfos()) { $pending.Push($child) } }
        }
        Remove-Item -LiteralPath $run -Recurse -Force
    }
    New-Item -ItemType Directory -Path $run | Out-Null
    [IO.File]::WriteAllText((Join-Path $run '.qualification-owned'), $owner)
    foreach ($directory in 'temp', 'localappdata', 'config') {
        New-Item -ItemType Directory -Path (Join-Path $run $directory) | Out-Null
    }
    $env:TEMP = Join-Path $run 'temp'; $env:TMP = $env:TEMP
    $env:LOCALAPPDATA = Join-Path $run 'localappdata'
    $env:XDG_CONFIG_HOME = Join-Path $run 'config'
    $env:SCOOP = Join-Path $run 'scoop'
    $env:SCOOP_GLOBAL = Join-Path $run 'global'
    $env:SCOOP_CACHE = Join-Path $run 'cache'
    [Net.WebRequest]::DefaultWebProxy = $null
    $global:VerbosePreference = 'SilentlyContinue'
    $global:ProgressPreference = 'SilentlyContinue'
    Import-Module Microsoft.PowerShell.Management, Microsoft.PowerShell.Utility, Microsoft.PowerShell.Security, Microsoft.PowerShell.Archive -ErrorAction Stop
    # Initialize the Windows socket provider before replacing HKLM; its protocol
    # catalog is real host infrastructure, outside the installer's environment keys.
    $metadata = Microsoft.PowerShell.Utility\Invoke-RestMethod -Uri ($identity.origin + '/metadata.json') -UseBasicParsing
    if ($metadata.ReleaseTag -cne 'v7.6.6') { throw 'The pinned transport fixture did not respond.' }
    Add-Type -Path (Join-Path $PSScriptRoot 'InstallerRegistryScope.cs')
    $scope = [PowerForge.Corpus.InstallerRegistryScope]::new($PSHOME)
    try {
        $scope.Verify()
        if (-not (Get-Item -LiteralPath HKCU:\).GetValue('QualificationOwner') -or
            -not (Get-Item -LiteralPath HKLM:\).GetValue('QualificationOwner')) { throw 'The registry provider did not enter the owned fixture.' }
        return [pscustomobject]@{root=$run;state=$state;registry=$scope;origin=$identity.origin}
    } catch { $scope.Dispose(); throw }
}

function Write-InstallerObservation {
    param([string] $ImportPath, [string] $Surface, $Module, [object[]] $Scenarios, [string] $ResultPath, [string] $Limits)
    if ($Module.ExportedCmdlets.Count) { throw 'The application adapter must expose authored functions only.' }
    [ordered]@{
        schemaVersion=1;surface=$Surface;importPath=[IO.Path]::GetFullPath($ImportPath)
        actualExports=@($Module.ExportedFunctions.Keys | Sort-Object)
        actualAliases=@($Module.ExportedAliases.Values | Sort-Object Name | ForEach-Object { [ordered]@{name=$_.Name;definition=$_.Definition} })
        scenarios=$Scenarios;limits=$Limits
    } | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $ResultPath -Encoding utf8
}

function New-InstallerScenario {
    param([string] $Id, [string] $SourcePath, [string[]] $Functions, $Observation)
    return [ordered]@{
        scenarioId=$Id;succeeded=$true;executedExports=@($Functions)
        executedOwnedFunctions=@($Functions | ForEach-Object { [ordered]@{relativePath=$SourcePath;name=$_} })
        observation=$Observation
    }
}
