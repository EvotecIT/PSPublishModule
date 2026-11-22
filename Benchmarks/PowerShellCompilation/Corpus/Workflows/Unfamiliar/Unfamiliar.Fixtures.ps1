function New-UnfamiliarCaseDirectory {
    param([string] $Root, [string] $Name)

    $directory = Join-Path $Root $Name
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    return $directory
}

function Write-UnfamiliarText {
    param([string] $Path, [AllowEmptyString()][string] $Text)

    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function New-UnfamiliarZip {
    param([string] $Path, [System.Collections.IDictionary] $Files)

    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $Files.Keys) {
            $stream = $archive.CreateEntry($file).Open()
            try {
                $bytes = $Files[$file]
                $stream.Write($bytes, 0, $bytes.Length)
            } finally {
                $stream.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }
}

function New-UnfamiliarTreeFixture {
    param([string] $Root)

    $tree = New-UnfamiliarCaseDirectory $Root 'tree'
    $data = New-UnfamiliarCaseDirectory $tree 'nested'
    $null = New-UnfamiliarCaseDirectory $tree 'empty'
    Write-UnfamiliarText (Join-Path $tree 'README.md') "fixture one
"
    Write-UnfamiliarText (Join-Path $data 'record.txt') "alpha
beta
"
    return $tree
}

function Set-UnfamiliarGitIsolation {
    param([hashtable] $Environment, [string] $Root)

    foreach ($name in [Environment]::GetEnvironmentVariables().Keys) {
        if ($name -like 'GIT_*') { $Environment[$name] = $null }
    }
    $globalConfig = Join-Path $Root '.fixture-gitconfig'
    if (-not (Test-Path -LiteralPath $globalConfig)) { Write-UnfamiliarText $globalConfig '' }
    $Environment.GIT_CONFIG_GLOBAL = $globalConfig
    $Environment.GIT_CONFIG_NOSYSTEM = '1'
    $Environment.GIT_CEILING_DIRECTORIES = Split-Path -Parent $Root
}

function New-UnfamiliarGitFixture {
    param([string] $Root)

    Write-UnfamiliarText (Join-Path $Root 'consolegit2gitfilters.json') '{"ContainsFilters":["/excluded"],"SuffixFilters":[".generated"],"PrefixFilters":["scratch"]}'
    $environment = @{}
    Set-UnfamiliarGitIsolation $environment $Root
    $environment.GIT_AUTHOR_NAME = 'Fixture Author'
    $environment.GIT_COMMITTER_NAME = 'Fixture Author'
    $environment.GIT_AUTHOR_EMAIL = 'author@example.test'
    $environment.GIT_COMMITTER_EMAIL = 'author@example.test'
    $environment.GIT_AUTHOR_DATE = '2025-01-01T00:00:00+00:00'
    $environment.GIT_COMMITTER_DATE = '2025-01-01T00:00:00+00:00'
    $steps = @(
        @{arguments = @('init', '--quiet', '--template=')}
        @{arguments = @('add', 'consolegit2gitfilters.json')}
        @{arguments = @('-c', 'commit.gpgsign=false', 'commit', '--quiet', '-m', 'Fixture changes (#123)')}
    )
    foreach ($step in $steps) {
        $result = Invoke-OwnedProcess -FileName 'git' -Arguments $step.arguments -WorkingDirectory $Root -Environment $environment -TimeoutSeconds 30
        if ($result.ExitCode -ne 0) { throw 'The checked local Git fixture could not be created.' }
    }
}

function New-UnfamiliarScriptArguments {
    param($Entry, [string] $Root, [string] $AcquiredRoot, [bool] $Positive)

    $missing = Join-Path $Root 'missing'
    switch ($Entry.id) {
        'check-ipv4-address' {
            '-Address'
            if ($Positive) { '192.0.2.57' } else { '192.0.2.999' }
        }
        'check-ps1-file' {
            $script = Join-Path $Root 'sample.ps1'
            $value = if ($Positive) { "'fixture'" } else { 'if (' }
            Write-UnfamiliarText $script $value
            $script
        }
        'count-characters' {
            if ($Positive) { 'alpha' } else { 'two words' }
        }
        'count-lines' {
            if ($Positive) { New-UnfamiliarTreeFixture $Root } else { $missing }
        }
        'get-sha512' {
            $file = Join-Path $Root 'record.txt'
            if ($Positive) { Write-UnfamiliarText $file 'fixture input' }
            $file
        }
        'list-calendar' {
            '-Month'
            if ($Positive) { '2' } else { '-2' }
            '-Year'
            '2024'
        }
        'list-dir-tree' {
            if ($Positive) { New-UnfamiliarTreeFixture $Root } else { $missing }
        }
        'list-empty-dirs' {
            if ($Positive) { New-UnfamiliarTreeFixture $Root } else { $missing }
        }
        'list-anagrams' {
            '-Word'
            if ($Positive) { 'abc' } else { 'aba' }
            '-Columns'
            '3'
        }
        'write-pi' {
            if ($Positive) { '12' } else { 'invalid' }
        }
        'convert-zip2dir' {
            $file = Join-Path $Root 'fixture.zip'
            if ($Positive) {
                New-UnfamiliarZip $file ([ordered]@{
                    'nested/record.txt' = [Text.Encoding]::UTF8.GetBytes('archive payload')
                    'README.md' = [Text.Encoding]::UTF8.GetBytes('fixture')
                })
            } else { Write-UnfamiliarText $file 'invalid archive' }
            [IO.Path]::GetFileName($file)
        }
        'GenerateParsingHelpers' {
            $schema = Join-Path $Root 'schema.json'
            $value = if ($Positive) {
                '{"ApiEnums":[{"Name":"DML_FIXTURE_MODE","Values":[]}],"ApiFlags":[],"Operators":[],"ActivationOperators":[]}'
            } else { '{]' }
            Write-UnfamiliarText $schema $value
            '-SchemaFilePath'
            $schema
            '-MaxFeatureLevel'
            '6.3'
        }
        'BuildScriptVersions' {
            # The script reads its own sibling dist directory. Its current executable
            # source-root admission prevents that layout from being delivered.
        }
        'Summarize-AsaResults' {
            $report = Join-Path $Root 'report.json'
            $value = if ($Positive) { '{"Metadata":{"compare-version":"1.0","compare-os":"Windows","compare-osversion":"fixture"},"Results":{}}' } else { '{]' }
            Write-UnfamiliarText $report $value
            '-Path'
            $report
        }
        'Get-OSSConhostLog' {
            New-UnfamiliarGitFixture $Root
            if ($Positive) { 'HEAD' } else { 'refs/heads/no-such-branch' }
        }
        'DirectoryDistributionChecker' {
            '-Path'
            if ($Positive) { New-UnfamiliarTreeFixture $Root } else { $missing }
            '-ExportFormat'
            'JSON'
            '-PassThru'
            '-OutputPath'
            (New-UnfamiliarCaseDirectory $Root 'reports')
            '-LogPath'
            (Join-Path $Root 'distribution.log')
        }
        'Format-SourceHygiene' {
            Write-UnfamiliarText (Join-Path $Root 'sample.cs') 'namespace Fixture;   '
            if ($Positive) { '-DryRun' } else { '-InvalidHygieneArgument' }
        }
        'Verify-MarkdownLinks' {
            $docs = New-UnfamiliarCaseDirectory $Root 'docs'
            Write-UnfamiliarText (Join-Path $docs 'target.md') '# Target'
            $path = if ($Positive) { 'target.md' } else { 'missing.md' }
            Write-UnfamiliarText (Join-Path $docs 'index.md') "# Fixture

[Local]($path)
"
            '-Path'
            $docs
        }
        'Build-MainSummaryFromZip' {
            $archive = Join-Path $Root 'bundle.zip'
            if ($Positive) {
                $member = Join-Path $Root 'ResourcesReport_2026.zip'
                New-UnfamiliarZip $member ([ordered]@{
                    'Inventory_fixture.json' = [Text.Encoding]::UTF8.GetBytes('{"Version":1,"VirtualMachines":[{"Subscription":"Fixture","Name":"VM-01"}]}')
                    'subscription.html' = [Text.Encoding]::UTF8.GetBytes('<!doctype html><title>Fixture</title>')
                })
                New-UnfamiliarZip $archive ([ordered]@{'ResourcesReport_2026.zip' = [IO.File]::ReadAllBytes($member)})
            } else { Write-UnfamiliarText $archive 'invalid archive' }
            '-InputZip'
            $archive
            '-OutputDirectory'
            (Join-Path $Root 'output')
            '-Detailed'
            '-KeepOriginalReports'
            '-PackageZip'
        }
        'ResxGen' {
            $manifest = Join-Path $Root 'EventSource.man'
            if ($Positive) {
                $archivePrefix = $Entry.entryPoint.Split('/')[0]
                $relativePath = "$archivePrefix/src/PowerShell.Core.Instrumentation/PowerShell.Core.Instrumentation.man"
                $source = Assert-ContainedPath -Root $AcquiredRoot -Path (Join-Path $AcquiredRoot $relativePath) -Label 'EventSource fixture'
                Copy-Item -LiteralPath $source -Destination $manifest
            } else { Write-UnfamiliarText $manifest '<assembly>' }
            $output = New-UnfamiliarCaseDirectory $Root 'output'
            '-Manifest'
            $manifest
            '-ResxPath'
            $output
            '-CodePath'
            $output
        }
        default { throw "No approved script scenario for '$($Entry.id)'." }
    }
}

function New-UnfamiliarCases {
    param($Entry, [string] $Root, [string] $AcquiredRoot)

    foreach ($name in @('success', 'alternate')) {
        $directory = New-UnfamiliarCaseDirectory $Root $name
        $positive = $name -eq 'success'
        if ($Entry.workloadKind -eq 'Module') {
            [pscustomobject]@{
                id = $name
                workingDirectory = $directory
                observerPath = Join-Path $PSScriptRoot 'Observe-UnfamiliarModule.ps1'
                arguments = @('-Workload', $Entry.id, '-Case', $name)
            }
        } else {
            [pscustomobject]@{
                id = $name
                workingDirectory = $directory
                arguments = @(New-UnfamiliarScriptArguments $Entry $directory $AcquiredRoot $positive)
            }
        }
    }
}


function Initialize-UnfamiliarOriginalCase {
    param($Entry, $Case, [string] $AuthoredPath, [string] $AcquiredRoot)

    if ($Entry.id -notin @('GenerateParsingHelpers', 'BuildScriptVersions', 'Format-SourceHygiene')) {
        return $AuthoredPath
    }
    $program = Join-Path $Case.workingDirectory 'program'
    $fixtureEntry = Assert-ContainedPath -Root $program -Path (Join-Path $program $Entry.entryPoint) -Label 'Owned case source'
    if ($Entry.id -eq 'Format-SourceHygiene') {
        Copy-WorkflowSourceInputs $Entry $AcquiredRoot $program
        Write-UnfamiliarText (Join-Path (Split-Path -Parent $fixtureEntry) 'sample.cs') 'namespace Fixture;   '
        return $fixtureEntry
    }

    $entryDirectory = Split-Path -Parent $fixtureEntry
    New-Item -ItemType Directory -Path $entryDirectory -Force | Out-Null
    Copy-Item -LiteralPath $AuthoredPath -Destination $fixtureEntry
    $project = Split-Path -Parent $entryDirectory
    if ($Entry.id -eq 'GenerateParsingHelpers') {
        $null = New-UnfamiliarCaseDirectory $project 'src/model'
    } elseif ($Case.id -eq 'success') {
        $dist = New-UnfamiliarCaseDirectory $project 'dist'
        Write-UnfamiliarText (Join-Path $dist 'ScriptVersions.csv') "File,Version
record.txt,1.2.3
"
        Write-UnfamiliarText (Join-Path $dist 'record.txt') 'checksum fixture'
    }
    return $fixtureEntry
}

function ConvertTo-UnfamiliarScenarioText {
    param([AllowEmptyString()][string] $Text, [string] $WorkloadId)

    if ($WorkloadId -in @('count-lines', 'list-empty-dirs', 'convert-zip2dir')) {
        return [regex]::Replace($Text, '(?<= in )\d+(?=\s*(?:s\.|sec\.))', '<elapsed>')
    }
    return $Text
}

function ConvertTo-UnfamiliarScenarioEffectHash {
    param([string] $WorkloadId, [string] $RelativePath, [string] $Root, [string] $RawHash,
        [datetime] $Started, [datetime] $Completed)

    if ($WorkloadId -ceq 'PSWriteHTML' -and $RelativePath -ceq 'report.html') {
        return ConvertTo-UnfamiliarReportEffectHash (Join-Path $Root $RelativePath) $Started $Completed
    }
    if ($WorkloadId -ceq 'PScribo' -and $RelativePath -ceq 'fixture.html') {
        return ConvertTo-SingleClassReportEffectHash (Join-Path $Root $RelativePath)
    }
    return $RawHash
}

function Get-UnfamiliarContentHash {
    param([byte[]] $Bytes)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha256.ComputeHash($Bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $sha256.Dispose() }
}

function ConvertTo-UnfamiliarReportEffectHash {
    param([string] $Path, [datetime] $Started, [datetime] $Completed)

    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $html = $utf8.GetString([IO.File]::ReadAllBytes($Path))
    $meta = [regex] '\A<meta (?:name="(?<name>viewport|revised)" content="(?<value>[^"]*)"|content="(?<value>[^"]*)" name="(?<name>viewport|revised)")>\z'
    $markup = [regex] @'
(?isx)
    <!-- (?: -?> | .*? (?: --!?> | \z ) )
  | <plaintext\b .* \z
  | <(?<raw>script|style|textarea|title|xmp|iframe|noembed|noframes|noscript)\b
       (?: [^>"'] | "[^"]*" | '[^']*' )* > .*? (?: </\k<raw>\s*> | \z )
  | <(?: [!?] | /?[a-z] ) (?: [^>"'] | "[^"]*" | '[^']*' )* >
'@
    $tags = @(
        foreach ($token in $markup.Matches($html)) {
            $tag = $meta.Match($token.Value)
            if ($tag.Success) {
                [pscustomobject]@{ Index = $token.Index; Length = $token.Length
                    Name = $tag.Groups['name'].Value; Value = $tag.Groups['value'].Value }
            }
        }
    )
    $revision = @($tags | Where-Object Name -CEQ 'revised')
    $viewport = @($tags | Where-Object Name -CEQ 'viewport')
    if ($revision.Count -ne 1 -or $viewport.Count -ne 1) {
        throw 'The HTML report must contain one viewport and one revision meta element.'
    }
    $timestamp = [DateTime]::ParseExact($revision[0].Value, 'yyyy-MM-dd HH:mm:ss',
        [Globalization.CultureInfo]::InvariantCulture)
    if ($timestamp -lt $Started.AddSeconds(-1) -or $timestamp -gt $Completed) {
        throw 'The HTML report revision timestamp falls outside the invocation.'
    }
    $content = $html
    foreach ($tag in ($tags | Sort-Object Index -Descending)) {
        $value = if ($tag.Name -ceq 'revised') { '<invocation-time>' } else { $tag.Value }
        $normalized = '<meta name="{0}" content="{1}">' -f $tag.Name, $value
        $content = $content.Remove($tag.Index, $tag.Length).Insert($tag.Index, $normalized)
    }
    return Get-UnfamiliarContentHash ($utf8.GetBytes($content))
}

function Get-UnfamiliarCssRuleEnds {
    param([string] $Css)

    $rules = [Collections.Generic.Dictionary[int, int]]::new()
    $delimiters = [Collections.Generic.Stack[char]]::new()
    $cssWhitespace = [char[]] @(0x20, 0x09, 0x0a, 0x0c, 0x0d)
    $quote = [char] 0
    $escaped = $false
    $start = -1
    for ($i = 0; $i -lt $Css.Length; $i++) {
        $c = $Css[$i]
        if ($escaped) { $escaped = $false; continue }
        if ($quote -eq [char] 0 -and $c -in $cssWhitespace) { continue }
        if ($start -lt 0) { $start = $i }
        if ($c -eq '\') { $escaped = $true; continue }
        if ($quote -ne [char] 0) {
            if ($c -eq $quote) { $quote = [char] 0 }
            continue
        }
        switch ($c) {
            { $_ -in '"', "'" } { $quote = $c }
            '{' { $delimiters.Push('}') }
            '(' { $delimiters.Push(')') }
            '[' { $delimiters.Push(']') }
            { $_ -in '}', ')', ']' } {
                if ($delimiters.Count -eq 0 -or $delimiters.Pop() -ne $c) {
                    throw 'The report comparison requires balanced CSS delimiters.'
                }
                if ($c -eq '}' -and $delimiters.Count -eq 0) {
                    $rules.Add($start, $i + 1)
                    $start = -1
                }
            }
            ';' {
                if ($delimiters.Count -eq 0 -and $Css[$start] -eq '@') { $start = -1 }
            }
        }
    }
    if ($start -ge 0 -or $delimiters.Count -gt 0 -or $quote -ne [char] 0 -or $escaped) {
        throw 'The report comparison requires balanced CSS delimiters and complete rules.'
    }
    return ,$rules
}

function ConvertTo-SingleClassReportEffectHash {
    param([string] $Path)

    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $html = $utf8.GetString([IO.File]::ReadAllBytes($Path))
    $styles = [regex]::Matches($html, '(?is)<style(?:\s+[^>]*)?>(?<css>.*?)</style>')
    $attributes = [regex]::Matches($html, '(?i)\bclass\s*=')
    $classes = [regex]::Matches($html, '(?i)\bclass\s*=\s*"(?<value>[-A-Za-z0-9_]+)"')
    if ($styles.Count -ne 1 -or $attributes.Count -ne $classes.Count -or
        [regex]::IsMatch($html, '(?i)<script\b')) {
        throw 'The report comparison requires one inline stylesheet and single-class elements with no scripts.'
    }

    $css = $styles[0].Groups['css']
    if ($css.Value.Contains('/*') -or $css.Value.Contains('*/')) {
        throw 'The report comparison requires a comment-free stylesheet.'
    }
    $rules = [regex]::Matches($css.Value, '(?m)^[\t ]*\.(?<name>[-A-Za-z0-9_]+)[\t ]*\{[^\r\n{}]*\}[\t ]*\r?\n')
    $ruleEnds = Get-UnfamiliarCssRuleEnds $css.Value
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $lines = [Collections.Generic.List[string]]::new()
    $end = if ($rules.Count) { $rules[0].Index } else { 0 }
    foreach ($rule in $rules) {
        $selector = $rule.Groups['name'].Index - 1
        if (-not $ruleEnds.ContainsKey($selector) -or $ruleEnds[$selector] -ne ($rule.Index + $rule.Value.TrimEnd().Length)) {
            throw 'The report comparison requires complete standalone single-class CSS rules.'
        }
        if ($rule.Index -ne $end -or -not $names.Add($rule.Groups['name'].Value)) {
            throw 'The report comparison requires a contiguous block of distinct single-class CSS rules.'
        }
        $lines.Add($rule.Value)
        $end += $rule.Length
    }
    if ($rules.Count -lt 2) {
        throw 'The report comparison could not identify its generated CSS rule block.'
    }

    $first = $css.Index + $rules[0].Index
    $sorted = $lines.ToArray()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $observed = $html.Substring(0, $first) + ($sorted -join '') + $html.Substring($css.Index + $end)
    return Get-UnfamiliarContentHash ($utf8.GetBytes($observed))
}
