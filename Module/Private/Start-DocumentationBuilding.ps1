function Start-DocumentationBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath,
        [string] $ProjectName
    )
    # Support for old way of building documentation -> converts to new one
    if ($Configuration.Steps.BuildDocumentation -is [bool]) {
        $TemporaryBuildDocumentation = $Configuration.Steps.BuildDocumentation
        $Configuration.Steps.BuildDocumentation = @{
            Enable = $TemporaryBuildDocumentation
        }
    }
    # Real documentation process
    if ($Configuration.Steps.BuildDocumentation -is [System.Collections.IDictionary]) {
        if ($Configuration.Steps.BuildDocumentation.Enable -eq $true) {
            $WarningVariablesMarkdown = @()
            $DocumentationPath = "$FullProjectPath\$($Configuration.Options.Documentation.Path)"
            $ReadMePath = "$FullProjectPath\$($Configuration.Options.Documentation.PathReadme)"
            Write-Text "[+] Generating documentation to $DocumentationPath with $ReadMePath" -Color Yellow

            if (-not (Test-Path -Path $DocumentationPath)) {
                $null = New-Item -Path "$FullProjectPath\Docs" -ItemType Directory -Force
            }
            if ($Configuration.Steps.BuildDocumentation.Tool -eq 'HelpOut') {
                try {
                    Save-MarkdownHelp -Module $ProjectName -OutputPath $DocumentationPath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue -SkipCommandType Alias -ExcludeFile "*.svg"
                } catch {
                    Write-Text "[-] Documentation warning: $($_.Exception.Message)" -Color Yellow
                }
            } else {
                [Array] $Files = Get-ChildItem -Path $DocumentationPath
                if ($Files.Count -gt 0) {
                    if ($Configuration.Steps.BuildDocumentation.StartClean -ne $true) {
                        try {
                            $null = Update-MarkdownHelpModule $DocumentationPath -RefreshModulePage -ModulePagePath $ReadMePath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue -ExcludeDontShow
                        } catch {
                            Write-Text "[-] Documentation warning: $($_.Exception.Message)" -Color Yellow
                        }
                    } else {
                        Remove-ItemAlternative -Path $DocumentationPath -SkipFolder
                        [Array] $Files = Get-ChildItem -Path $DocumentationPath
                    }
                }
                if ($Files.Count -eq 0) {
                    try {
                        $null = New-MarkdownHelp -Module $ProjectName -WithModulePage -OutputFolder $DocumentationPath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue -ExcludeDontShow
                    } catch {
                        Write-Text "[-] Documentation warning: $($_.Exception.Message)" -Color Yellow
                    }
                    $null = Move-Item -Path "$DocumentationPath\$ProjectName.md" -Destination $ReadMePath -ErrorAction SilentlyContinue
                    #Start-Sleep -Seconds 1
                    # this is temporary workaround - due to diff output on update
                    if ($Configuration.Steps.BuildDocumentation.UpdateWhenNew) {
                        try {
                            $null = Update-MarkdownHelpModule $DocumentationPath -RefreshModulePage -ModulePagePath $ReadMePath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue -ExcludeDontShow
                        } catch {
                            Write-Text "[-] Documentation warning: $($_.Exception.Message)" -Color Yellow
                        }
                    }
                }
            }
            foreach ($_ in $WarningVariablesMarkdown) {
                Write-Text "[-] Documentation warning: $_" -Color Yellow
            }
        }
    }
}