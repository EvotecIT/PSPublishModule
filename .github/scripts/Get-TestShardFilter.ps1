#requires -Version 7.4
<#
.SYNOPSIS
Selects one deterministic shard of a filtered xUnit test set.
.DESCRIPTION
Lists the test cases matched by BaseFilter from an already built project and assigns
their sorted display names round-robin, so every case (including each theory row)
lands in exactly one shard and slow classes or theories spread across all shards.
Writes the shard filter into a run settings file, because `dotnet test --filter`
passes through MSBuild and drops the quotes theory display names contain. Fails
when the shard would select nothing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Project,
    [Parameter(Mandatory)] [string] $BaseFilter,
    [Parameter(Mandatory)] [ValidateRange(1, 64)] [int] $ShardCount,
    [Parameter(Mandatory)] [ValidateRange(1, 64)] [int] $ShardNumber,
    [Parameter(Mandatory)] [string] $RunSettingsPath,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
if ($ShardNumber -gt $ShardCount) { throw "Shard $ShardNumber is outside 1..$ShardCount." }

$listing = & dotnet test $Project -c $Configuration --no-build --no-restore --nologo --list-tests --filter $BaseFilter 2>&1
if ($LASTEXITCODE -ne 0) {
    $listing | Write-Host
    throw "Test discovery failed with exit code $LASTEXITCODE."
}

$started = $false
$cases = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($line in $listing) {
    $text = "$line"
    if ($text -match '^The following Tests are available:') { $started = $true; continue }
    if (-not $started -or $text -notmatch '^\s{4}(\S.*)$') { continue }
    if (-not $cases.Add($Matches[1].TrimEnd())) { throw "Duplicate test display name: $($Matches[1])" }
}
if ($cases.Count -eq 0) { throw "No tests matched '$BaseFilter'." }

$selected = [Collections.Generic.List[string]]::new()
$position = 0
foreach ($case in $cases) {
    if (($position % $ShardCount) -eq ($ShardNumber - 1)) { $selected.Add($case) }
    $position++
}
if ($selected.Count -eq 0) { throw "Shard $ShardNumber of $ShardCount selected no tests from $($cases.Count)." }

# VSTest filter values escape these characters with a backslash.
$escape = { param($value) [regex]::Replace($value, '[\\()&|=!~]', { param($match) '\' + $match.Value }) }
$filter = "($BaseFilter)&(" + (($selected | ForEach-Object { "DisplayName=$(& $escape $_)" }) -join '|') + ')'
$settings = [Xml.XmlDocument]::new()
$runSettings = $settings.AppendChild($settings.CreateElement('RunSettings'))
$runConfiguration = $runSettings.AppendChild($settings.CreateElement('RunConfiguration'))
$runConfiguration.AppendChild($settings.CreateElement('TestCaseFilter')).InnerText = $filter
$settings.Save([IO.Path]::GetFullPath($RunSettingsPath))
Write-Host "Shard $ShardNumber/$ShardCount runs $($selected.Count) of $($cases.Count) tests."
