function Invoke-PowerForgeAnonymousGit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $IsolationRoot
    )

    if (-not (Test-Path -LiteralPath $IsolationRoot -PathType Container)) {
        throw 'Anonymous Git isolation root must be an existing directory.'
    }

    $emptyGlobalConfig = Join-Path $IsolationRoot 'anonymous.gitconfig'
    if (-not (Test-Path -LiteralPath $emptyGlobalConfig -PathType Leaf)) {
        Set-Content -LiteralPath $emptyGlobalConfig -Value '' -Encoding utf8NoBOM -NoNewline
    }

    if ($IsWindows) {
        $askPass = Join-Path $IsolationRoot 'anonymous-git-askpass.cmd'
        if (-not (Test-Path -LiteralPath $askPass -PathType Leaf)) {
            Set-Content -LiteralPath $askPass -Value "@echo off`r`nexit /b 1`r`n" -Encoding ascii -NoNewline
        }
    } else {
        $askPass = Join-Path $IsolationRoot 'anonymous-git-askpass.sh'
        if (-not (Test-Path -LiteralPath $askPass -PathType Leaf)) {
            Set-Content -LiteralPath $askPass -Value "#!/bin/sh`nexit 1`n" -Encoding utf8NoBOM -NoNewline
            [IO.File]::SetUnixFileMode(
                $askPass,
                [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserExecute
            )
        }
    }

    $controlledNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in Get-ChildItem Env:) {
        if ($item.Name.StartsWith('GIT_', [StringComparison]::OrdinalIgnoreCase) -or
            $item.Name.StartsWith('GCM_', [StringComparison]::OrdinalIgnoreCase) -or
            $item.Name.StartsWith('SSH_ASKPASS', [StringComparison]::OrdinalIgnoreCase)) {
            [void]$controlledNames.Add($item.Name)
        }
    }
    foreach ($name in @(
        'GIT_ASKPASS', 'SSH_ASKPASS', 'GIT_TERMINAL_PROMPT', 'GCM_INTERACTIVE',
        'GIT_CONFIG_NOSYSTEM', 'GIT_CONFIG_GLOBAL', 'GIT_CONFIG_SYSTEM',
        'GIT_CONFIG', 'GIT_CONFIG_PARAMETERS', 'GIT_CONFIG_COUNT'
    )) {
        [void]$controlledNames.Add($name)
    }

    $previousValues = @{}
    try {
        foreach ($name in $controlledNames) {
            $previousValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
        $env:GIT_ASKPASS = $askPass
        $env:SSH_ASKPASS = $askPass
        $env:GIT_TERMINAL_PROMPT = '0'
        $env:GCM_INTERACTIVE = 'Never'
        $env:GIT_CONFIG_NOSYSTEM = '1'
        $env:GIT_CONFIG_GLOBAL = $emptyGlobalConfig
        $env:GIT_CONFIG_COUNT = '0'

        $gitArguments = @(
            '-c', 'credential.helper=',
            '-c', "core.askPass=$askPass",
            '-c', 'http.extraHeader='
        ) + $Arguments
        $output = @(& git @gitArguments 2>$null)
        [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = $output
        }
    } finally {
        foreach ($name in $controlledNames) {
            [Environment]::SetEnvironmentVariable($name, $previousValues[$name], 'Process')
        }
    }
}
