# PSPublishModule bootstrapper (script module)
# Loads binary cmdlets (preferred) and optionally dot-sources script helpers when present.
try {
    if (-not [Console]::IsOutputRedirected -and -not [Console]::IsErrorRedirected) {
        $utf8 = [System.Text.UTF8Encoding]::new($false)
        [Console]::InputEncoding = $utf8
        [Console]::OutputEncoding = $utf8
    }
} catch {
    # best effort only
}

# Get public and private function definition files.
$Public  = @(Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, 'Public', '*.ps1')) -ErrorAction SilentlyContinue -Recurse)
$Private = @(Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, 'Private', '*.ps1')) -ErrorAction SilentlyContinue -Recurse)
$Classes = @(Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, 'Classes', '*.ps1')) -ErrorAction SilentlyContinue -Recurse)
$Enums   = @(Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, 'Enums', '*.ps1')) -ErrorAction SilentlyContinue -Recurse)

$LibPath = [IO.Path]::Combine($PSScriptRoot, 'Lib')

$FoundErrors = @(
    # Import binary module (if present) to expose cmdlets/types.
    try {
        $BinaryModule = $null

        if (Test-Path -LiteralPath $LibPath) {
            if ($PSEdition -eq 'Core') {
                $BinaryModule = [IO.Path]::Combine($LibPath, 'Core', 'PSPublishModule.dll')
            } else {
                $BinaryModule = [IO.Path]::Combine($LibPath, 'Default', 'PSPublishModule.dll')
            }
        }

        if (-not $BinaryModule -or -not (Test-Path -LiteralPath $BinaryModule)) {
            $BinaryModule = [IO.Path]::Combine($PSScriptRoot, 'PSPublishModule.dll')
        }

        # Dev-mode: when running from the repo, prefer the freshly built project output over stale Lib binaries.
        # (Lib/* is gitignored and can easily become outdated during migration.)
        try {
            $repoRoot = Resolve-Path -LiteralPath ([IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..')))
            $dotnetMajor = [System.Environment]::Version.Major
            $tfms = if ($PSEdition -eq 'Core') {
                if ($dotnetMajor -ge 10) { @('net10.0', 'net8.0') } else { @('net8.0') }
            } else { @('net472') }
            $devBinary = $null
            foreach ($tfm in $tfms) {
                foreach ($cfg in @('Release', 'Debug')) {
                    $candidate = [IO.Path]::Combine($repoRoot, 'PSPublishModule', 'bin', $cfg, $tfm, 'PSPublishModule.dll')
                    if (Test-Path -LiteralPath $candidate) { $devBinary = $candidate; break }
                }
                if ($devBinary) { break }
            }

            if ($devBinary) {
                # When running from the repo, always prefer the project output over Lib binaries.
                # Lib/* is commonly stale during development and can mask code changes.
                $BinaryModule = $devBinary
            }
        } catch {
            # ignore and keep default resolution
        }

        # If PSPublishModule is already loaded in this session, reuse the already-loaded assembly
        # location to avoid "Assembly with same name is already loaded" errors on re-import.
        try {
            $loaded = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'PSPublishModule' } | Select-Object -First 1
            if ($loaded -and $loaded.Location -and (Test-Path -LiteralPath $loaded.Location)) {
                $BinaryModule = $loaded.Location
            }
        } catch {
            # ignore
        }

        if (Test-Path -LiteralPath $BinaryModule) {
            Import-Module -Name $BinaryModule -Force -ErrorAction Stop | Out-Null
        }
    } catch {
        Write-Warning "Failed to import binary module: $($_.Exception.Message)"
        $true
    }

    # Dot source the files (Private first).
    foreach ($Import in @($Private + $Public + $Classes + $Enums)) {
        try {
            . $Import.Fullname
        } catch {
            Write-Error -Message "Failed to import functions from $($import.Fullname): $_"
            $true
        }
    }
)

if ($FoundErrors.Count -gt 0) {
    $ModuleName = (Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, '*.psd1'))).BaseName
    Write-Warning "Importing module $ModuleName failed. Fix errors before continuing."
    break
}

# Export only public functions to avoid leaking Private helpers.
$ExportFunctions = @($Public | ForEach-Object { $_.BaseName })
$ExportAliases   = @('New-PrepareModule', 'Build-Module', 'Invoke-ModuleBuilder')

# Ensure backwards-compatible aliases exist even when legacy public functions are removed.
foreach ($AliasName in $ExportAliases) {
    try {
        Set-Alias -Name $AliasName -Value 'Invoke-ModuleBuild' -Scope Local -Force -ErrorAction Stop
    } catch {
        # best-effort
    }
}
Export-ModuleMember -Function $ExportFunctions -Alias $ExportAliases -Cmdlet '*'
