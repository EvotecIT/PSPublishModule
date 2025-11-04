function Approve-RequiredModules {
    [CmdletBinding()]
    param(
        [Array] $ApprovedModules,
        [Array] $ModulesToCheck,
        [Array] $RequiredModules,
        [Array] $DependantRequiredModules,
        [System.Collections.IDictionary] $MissingFunctions,
        [System.Collections.IDictionary] $Configuration,
        [Array] $CommandsWithoutModule
    )

    $TerminateEarly = $false

    Write-TextWithTime -Text "Pre-Verification of approved modules" {
        foreach ($ApprovedModule in $ApprovedModules) {
            $ApprovedModuleStatus = Get-Module -Name $ApprovedModule -ListAvailable
            if ($ApprovedModuleStatus) {
                Write-Text "   [>] Approved module $ApprovedModule exists - can be used for merging." -Color Green
            } else {
                Write-Text "   [>] Approved module $ApprovedModule doesn't exists. Potentially issue with merging." -Color Red
            }
        }
    } -PreAppend Plus

    Write-TextWithTime -Text "Analyze required, approved modules" {
        foreach ($Module in $ModulesToCheck.Source | Sort-Object) {
            if ($Module -in $RequiredModules -and $Module -in $ApprovedModules) {
                Write-Text "   [+] Module $Module is in required modules with ability to merge." -Color DarkYellow
                $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module })
                foreach ($F in $MyFunctions) {
                    if ($F.IsPrivate) {
                        Write-Text "      [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsPrivate: $($F.IsPrivate))" -Color Magenta
                    } else {
                        Write-Text "      [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsPrivate: $($F.IsPrivate))" -Color DarkYellow
                    }
                }
            } elseif ($Module -in $DependantRequiredModules -and $Module -in $ApprovedModules) {
                Write-Text "   [+] Module $Module is in dependant required module within required modules with ability to merge." -Color DarkYellow
                $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module })
                foreach ($F in $MyFunctions) {
                    Write-Text "      [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color DarkYellow
                }
            } elseif ($Module -in $DependantRequiredModules) {
                Write-Text "   [+] Module $Module is in dependant required module within required modules." -Color DarkGray
                $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module })
                foreach ($F in $MyFunctions) {
                    Write-Text "      [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color DarkGray
                }
            } elseif ($Module -in $RequiredModules) {
                Write-Text "   [+] Module $Module is in required modules." -Color Green
                $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module })
                foreach ($F in $MyFunctions) {
                    Write-Text "      [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color Green
                }
            } elseif ($Module -notin $RequiredModules -and $Module -in $ApprovedModules) {
                Write-Text "   [+] Module $Module is missing in required module, but it's in approved modules." -Color Magenta
                $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module })
                foreach ($F in $MyFunctions) {
                    Write-Text "      [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color Magenta
                }
            } else {
                [Array] $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module })
                if ($Configuration.Options.Merge.ModuleSkip.Force -eq $true) {
                    Write-Text "   [-] Module $Module is missing in required modules. Non-critical issue as per configuration (force used)." -Color Gray
                    foreach ($F in $MyFunctions) {
                        Write-Text "      [>] Command affected $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate)). Ignored by configuration." -Color Gray
                    }
                } else {
                    if ($Module -in $Configuration.Options.Merge.ModuleSkip.IgnoreModuleName) {
                        Write-Text "   [-] Module $Module is missing in required modules. Non-critical issue as per configuration (skipped module)." -Color Gray
                        foreach ($F in $MyFunctions) {
                            Write-Text "      [>] Command affected $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate)). Ignored by configuration." -Color Gray
                        }
                    } else {
                        $FoundProblem = $false
                        foreach ($F in $MyFunctions) {
                            if ($F.Name -notin $Configuration.Options.Merge.ModuleSkip.IgnoreFunctionName) {
                                $FoundProblem = $true
                            }
                        }
                        if (-not $FoundProblem) {
                            Write-Text "   [-] Module $Module is missing in required modules. Non-critical issue as per configuration (skipped functions)." -Color Gray
                            foreach ($F in $MyFunctions) {
                                if ($F.Name -in $Configuration.Options.Merge.ModuleSkip.IgnoreFunctionName) {
                                    Write-Text "      [>] Command affected $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate)). Ignored by configuration." -Color Gray
                                } else {
                                    Write-Text "      [>] Command affected $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color Red
                                }
                            }
                        } else {
                            $TerminateEarly = $true
                            Write-Text "   [-] Module $Module is missing in required modules. Potential issue. Fix configuration required." -Color Red
                            foreach ($F in $MyFunctions) {
                                if ($F.Name -in $Configuration.Options.Merge.ModuleSkip.IgnoreFunctionName) {
                                    Write-Text "      [>] Command affected $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate)). Ignored by configuration." -Color Gray
                                } else {
                                    Write-Text "      [>] Command affected $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color Red
                                }
                            }
                        }
                    }
                }
            }
        }

        if ($CommandsWithoutModule.Count -gt 0) {
            $FoundProblem = $false
            foreach ($F in $CommandsWithoutModule) {
                if ($F.Name -notin $Configuration.Options.Merge.ModuleSkip.IgnoreFunctionName) {
                    $FoundProblem = $true
                }
            }
            if ($FoundProblem) {
                Write-Text "   [-] Some commands couldn't be resolved to functions (private function maybe?). Potential issue." -Color Red
                foreach ($F in $CommandsWithoutModule) {
                    if ($F.Name -notin $Configuration.Options.Merge.ModuleSkip.IgnoreFunctionName) {
                        $TerminateEarly = $true
                        Write-Text "      [>] Command affected $($F.Name) (Command Type: Unknown / IsAlias: $($F.IsAlias))" -Color Red
                    } else {
                        Write-Text "      [>] Command affected $($F.Name) (Command Type: Unknown / IsAlias: $($F.IsAlias)). Ignored by configuration." -Color Gray
                    }
                }
            } else {
                Write-Text "   [-] Some commands couldn't be resolved to functions (private function maybe?). Non-critical issue as per configuration (skipped functions)." -Color Gray
                foreach ($F in $CommandsWithoutModule) {
                    if ($F.Name -in $Configuration.Options.Merge.ModuleSkip.IgnoreFunctionName) {
                        Write-Text "      [>] Command affected $($F.Name) (Command Type: Unknown / IsAlias: $($F.IsAlias)). Ignored by configuration." -Color Gray
                    } else {
                        # this shouldn't happen, but just in case
                        Write-Text "      [>] Command affected $($F.Name) (Command Type: Unknown / IsAlias: $($F.IsAlias))" -Color Red
                    }
                }
            }
        }
        if ($TerminateEarly) {
            Write-Text "   [-] Some commands are missing in required modules. Fix this issue or use New-ConfigurationModuleSkip to skip verification." -Color Red
            return $false
        }

    } -PreAppend Plus
}