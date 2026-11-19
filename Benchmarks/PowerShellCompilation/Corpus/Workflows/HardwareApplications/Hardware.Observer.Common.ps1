function Initialize-HardwareObservation {
    param([string] $ScenarioRoot, [string] $Instance)
    $root = [IO.Path]::GetFullPath($ScenarioRoot)
    for ($current = Get-Item -LiteralPath $root; $null -ne $current; $current = $current.Parent) {
        if ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Hardware observation roots cannot traverse links.' }
    }
    $run = Join-Path $root ('surfaces/' + $Instance + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $run, (Join-Path $run 'temp'), (Join-Path $run 'localappdata') -Force | Out-Null
    $env:TEMP = Join-Path $run 'temp'; $env:TMP = $env:TEMP
    $env:LOCALAPPDATA = Join-Path $run 'localappdata'
    $env:PSModuleAnalysisCachePath = Join-Path $run 'module-cache'
    $env:COMPUTERNAME = 'FixtureComputer'
    $global:HardwareTrace = [Collections.Generic.List[string]]::new()
    $global:HardwareVendor = 'Dell'
    $global:HardwareProviderFailure = $false
    $global:HardwareAllowPrincipal = $true
    $global:HardwareRoot = $run
    $global:VerbosePreference = 'SilentlyContinue'
    $global:ProgressPreference = 'SilentlyContinue'
    return $run
}

function New-HardwareScenario {
    param([string] $Id, [string] $SourcePath, [string[]] $Functions, [string[]] $Exports, $Observation)
    [ordered]@{scenarioId=$Id;succeeded=$true;executedExports=$Exports
        executedOwnedFunctions=@($Functions|ForEach-Object {[ordered]@{relativePath=$SourcePath;name=$_}})
        observation=$Observation}
}

function Write-HardwareObservation {
    param([string] $ImportPath, [string] $Surface, $Module, [object[]] $Scenarios, [string] $ResultPath, [string] $Limits)
    $exports=@();$aliases=@()
    if($null -ne $Module){
        $exports=@($Module.ExportedFunctions.Keys|Sort-Object)
        $aliases=@($Module.ExportedAliases.Values|Sort-Object Name|ForEach-Object {[ordered]@{name=$_.Name;definition=$_.Definition}})
    }
    [ordered]@{schemaVersion=1;surface=$Surface;importPath=[IO.Path]::GetFullPath($ImportPath)
        actualExports=$exports;actualAliases=$aliases
        scenarios=$Scenarios;limits=$Limits}|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $ResultPath -Encoding utf8
}
