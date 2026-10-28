function Assert-PowerForgeCallerGitCommit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Workspace,
        [Parameter(Mandatory)][string] $CallerRepository,
        [Parameter(Mandatory)][string] $RepositoryRef
    )

    if ($RepositoryRef -notmatch '^([a-fA-F0-9]{40}|[a-fA-F0-9]{64})$') {
        throw 'Caller recovery repository must be pinned to an exact commit.'
    }
    & git -C $Workspace cat-file -e "${RepositoryRef}^{commit}" *> $null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    # A full checkout still omits commits that only belonged to deleted branches.
    # Reuse the caller checkout's read access, never a manifest-selected remote.
    $origins = @(& git -C $Workspace config --local --get-all remote.origin.url 2>$null)
    $slug = [regex]::Escape($CallerRepository)
    $originPattern = "^(?:https://github\.com/$slug(?:\.git)?/?|ssh://git@github\.com(?::22)?/$slug(?:\.git)?/?|git@github\.com:$slug(?:\.git)?)$"
    if ($LASTEXITCODE -ne 0 -or $origins.Count -ne 1 -or $origins[0] -notmatch $originPattern) {
        throw 'Missing caller recovery commit cannot be fetched from a different repository origin.'
    }

    $terminalPrompt = $env:GIT_TERMINAL_PROMPT
    $credentialInteraction = $env:GCM_INTERACTIVE
    try {
        $env:GIT_TERMINAL_PROMPT = '0'
        $env:GCM_INTERACTIVE = 'Never'
        # Do not move HEAD, rewrite refs, or overwrite FETCH_HEAD during validation.
        & git -C $Workspace -c maintenance.auto=false -c gc.auto=0 fetch `
            --no-tags --no-write-fetch-head --depth=1 origin $RepositoryRef *> $null
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to retrieve the exact pinned caller recovery commit from its repository origin.'
        }
        & git -C $Workspace cat-file -e "${RepositoryRef}^{commit}" *> $null
        if ($LASTEXITCODE -ne 0) {
            throw 'The retrieved caller recovery object is not the exact pinned commit.'
        }
    } finally {
        $env:GIT_TERMINAL_PROMPT = $terminalPrompt
        $env:GCM_INTERACTIVE = $credentialInteraction
    }
}
