function Get-PowerShellFileCompatibility {
    <#
    .SYNOPSIS
    Analyzes a single PowerShell file for compatibility with PowerShell 5.1 and PowerShell 7.

    .DESCRIPTION
    Examines PowerShell file content to detect version-specific features, cmdlets, and patterns.
    Identifies potential compatibility issues and provides recommendations.

    .PARAMETER FilePath
    Path to the PowerShell file to analyze.

    .EXAMPLE
    Get-PowerShellFileCompatibility -FilePath 'C:\Scripts\MyScript.ps1'
    Analyzes the specified PowerShell file for compatibility issues.

    .NOTES
    This function is used internally by Get-PowerShellCompatibility.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $FilePath
    )

    # Initialize result object
    $result = [PSCustomObject]@{
        FullPath = $FilePath
        RelativePath = [System.IO.Path]::GetFileName($FilePath)
        PowerShell51Compatible = $true
        PowerShell7Compatible = $true
        Encoding = $null
        Issues = @()
    }

    try {
        # Get file info
        $fileInfo = Get-Item -LiteralPath $FilePath -Force
        $result.RelativePath = $fileInfo.Name

        # Check encoding
        $encoding = Get-FileEncoding -Path $FilePath
        $result.Encoding = $encoding

        # Check encoding compatibility
        $hasSpecialChars = $false
        $specialCharsFound = @()

        if ($encoding -eq 'UTF8' -or $encoding -eq 'ASCII') {
            # Check for special characters, emojis, or non-ASCII content
            $contentBytes = [System.IO.File]::ReadAllBytes($FilePath)
            for ($i = 0; $i -lt $contentBytes.Length; $i++) {
                if ($contentBytes[$i] -gt 127) {
                    $hasSpecialChars = $true
                    # Try to get the character for reporting
                    try {
                        $char = [System.Text.Encoding]::UTF8.GetString($contentBytes[$i..($i+2)])
                        if ($char -match '[^\x00-\x7F]') {
                            $specialCharsFound += $char
                        }
                    } catch {
                        # Skip if we can't decode the character
                    }
                    break
                }
            }

            if ($encoding -eq 'UTF8' -and $hasSpecialChars) {
                $result.Issues += [PSCustomObject]@{
                    Type = 'Encoding'
                    Description = 'UTF8 without BOM may cause issues in PowerShell 5.1 with special characters'
                    Recommendation = 'Consider using UTF8BOM encoding for cross-version compatibility'
                    Severity = 'Medium'
                }
            } elseif ($encoding -eq 'ASCII' -and $hasSpecialChars) {
                $result.Issues += [PSCustomObject]@{
                    Type = 'Encoding'
                    Description = 'ASCII encoding with special characters will cause issues in PowerShell 5.1'
                    Recommendation = 'Convert to UTF8BOM encoding to properly handle special characters'
                    Severity = 'High'
                }
            }
            # If ASCII with no special chars, it's fine - no issue reported
        }

        # Read file content
        $content = Get-Content -LiteralPath $FilePath -Raw -ErrorAction Stop
        if (-not $content) {
            return $result
        }

        # Analyze PowerShell 7 specific features
        $ps7Features = @(
            @{
                Pattern = '(?m)^using\s+namespace\s+'
                Name = 'Using Namespace'
                Description = 'Using namespace directive is not supported in PowerShell 5.1'
            },
            @{
                Pattern = '\$\?\?\?'
                Name = 'Null Coalescing'
                Description = 'Null coalescing operator (??) is PowerShell 7+ only'
            },
            @{
                Pattern = '\$\?\.|\$\?\['
                Name = 'Null Conditional'
                Description = 'Null conditional operators (?. and ?[) are PowerShell 7+ only'
            },
            @{
                Pattern = '\|\|\?'
                Name = 'Null Coalescing Assignment'
                Description = 'Null coalescing assignment operator (??=) is PowerShell 7+ only'
            },

            @{
                Pattern = '\bGet-Error\b'
                Name = 'Get-Error Cmdlet'
                Description = 'Get-Error cmdlet is PowerShell 7+ only'
            },
            @{
                Pattern = '\bConvertTo-Json\b.*-AsArray'
                Name = 'ConvertTo-Json -AsArray'
                Description = 'ConvertTo-Json -AsArray parameter is PowerShell 7+ only'
            },
            @{
                Pattern = '\bTest-Json\b'
                Name = 'Test-Json Cmdlet'
                Description = 'Test-Json cmdlet is PowerShell 6+ only'
            },
            @{
                Pattern = '\bGet-Content\b.*-AsByteStream'
                Name = 'Get-Content -AsByteStream'
                Description = 'Get-Content -AsByteStream parameter is PowerShell 7+ only (use -Encoding Byte in PS 5.1)'
            },
            @{
                Pattern = '\bInvoke-RestMethod\b.*-Resume'
                Name = 'Invoke-RestMethod -Resume'
                Description = 'Invoke-RestMethod -Resume parameter is PowerShell 7+ only'
            }
        )

        # Analyze PowerShell 5.1 specific features
        $ps51Features = @(
            @{
                Pattern = '\bAdd-PSSnapin\b'
                Name = 'Add-PSSnapin'
                Description = 'Add-PSSnapin is deprecated in PowerShell 7 (use modules instead)'
            },
            @{
                Pattern = '\bGet-WmiObject\b'
                Name = 'Get-WmiObject'
                Description = 'Get-WmiObject is deprecated in PowerShell 7 (use Get-CimInstance instead)'
            },
            @{
                Pattern = '\bSet-WmiInstance\b'
                Name = 'Set-WmiInstance'
                Description = 'Set-WmiInstance is deprecated in PowerShell 7 (use Set-CimInstance instead)'
            },
            @{
                Pattern = '\bRemove-WmiObject\b'
                Name = 'Remove-WmiObject'
                Description = 'Remove-WmiObject is deprecated in PowerShell 7 (use Remove-CimInstance instead)'
            },
            @{
                Pattern = '\bInvoke-WmiMethod\b'
                Name = 'Invoke-WmiMethod'
                Description = 'Invoke-WmiMethod is deprecated in PowerShell 7 (use Invoke-CimMethod instead)'
            },
            @{
                Pattern = '\bGet-Content\b.*-Encoding\s+Byte'
                Name = 'Get-Content -Encoding Byte'
                Description = 'Get-Content -Encoding Byte is deprecated in PowerShell 7 (use -AsByteStream instead)'
            },
            @{
                Pattern = '\[\s*System\.Web\.HttpUtility\s*\]'
                Name = 'System.Web.HttpUtility'
                Description = 'System.Web.HttpUtility requires .NET Framework (not available in PowerShell 7 by default)'
            },
            @{
                Pattern = '\$PSVersionTable\.PSEdition\s*-eq\s*[''"]Desktop[''"]'
                Name = 'Desktop Edition Check'
                Description = 'Desktop edition is PowerShell 5.1 only'
            }
        )

        # Platform-specific features
        $platformFeatures = @(
            @{
                Pattern = '\bGet-EventLog\b'
                Name = 'Get-EventLog'
                Description = 'Get-EventLog is Windows-only and not available in PowerShell 7 on other platforms'
            },
            @{
                Pattern = '\bGet-Counter\b'
                Name = 'Get-Counter'
                Description = 'Get-Counter is Windows-only'
            },
            @{
                Pattern = '\bGet-Service\b'
                Name = 'Get-Service'
                Description = 'Get-Service works differently across platforms in PowerShell 7'
            },
            @{
                Pattern = '\bGet-Process\b.*-ComputerName'
                Name = 'Get-Process -ComputerName'
                Description = 'Get-Process -ComputerName is not available in PowerShell 7'
            },
            @{
                Pattern = '\bRegister-ObjectEvent\b'
                Name = 'Register-ObjectEvent'
                Description = 'Register-ObjectEvent may not work consistently across platforms'
            }
        )

        # Check for PowerShell 7 specific features
        foreach ($feature in $ps7Features) {
            if ($content -match $feature.Pattern) {
                $result.PowerShell51Compatible = $false
                $result.Issues += [PSCustomObject]@{
                    Type = 'PowerShell7Feature'
                    Description = "$($feature.Name): $($feature.Description)"
                    Recommendation = 'Consider using alternative syntax for PowerShell 5.1 compatibility'
                    Severity = 'High'
                }
            }
        }

        # Check for PowerShell 5.1 specific features
        foreach ($feature in $ps51Features) {
            if ($content -match $feature.Pattern) {
                $result.PowerShell7Compatible = $false
                $result.Issues += [PSCustomObject]@{
                    Type = 'PowerShell51Feature'
                    Description = "$($feature.Name): $($feature.Description)"
                    Recommendation = 'Consider updating to PowerShell 7 compatible alternatives'
                    Severity = 'High'
                }
            }
        }

        # Check for platform-specific features
        foreach ($feature in $platformFeatures) {
            if ($content -match $feature.Pattern) {
                $result.Issues += [PSCustomObject]@{
                    Type = 'PlatformSpecific'
                    Description = "$($feature.Name): $($feature.Description)"
                    Recommendation = 'Consider cross-platform alternatives or add platform checks'
                    Severity = 'Medium'
                }
            }
        }

        # Check for .NET Framework specific assemblies
        $dotNetFrameworkAssemblies = @(
            'System.Web',
            'System.Web.Extensions',
            'System.Configuration',
            'System.ServiceProcess',
            'System.Management.Automation.dll'
        )

        foreach ($assembly in $dotNetFrameworkAssemblies) {
            if ($content -match [regex]::Escape($assembly)) {
                $result.Issues += [PSCustomObject]@{
                    Type = 'DotNetFramework'
                    Description = "$assembly assembly may not be available in PowerShell 7"
                    Recommendation = 'Verify assembly availability or find .NET Core/.NET 5+ alternatives'
                    Severity = 'Medium'
                }
            }
        }

        # Check for class definitions (PowerShell 5.0+)
        if ($content -match '(?m)^class\s+\w+') {
            # Classes are supported in both PS 5.1 and PS 7, but check for issues
            if ($content -match '(?m)^class\s+\w+\s*:\s*System\.') {
                $result.Issues += [PSCustomObject]@{
                    Type = 'ClassInheritance'
                    Description = 'Class inheritance from System types may behave differently between versions'
                    Recommendation = 'Test class behavior across PowerShell versions'
                    Severity = 'Low'
                }
            }
        }

        # DSC works in both PowerShell 5.1 and PowerShell 7, so no compatibility check needed

        # Check for workflows (PowerShell 5.1 only)
        if ($content -match '(?m)^workflow\s+\w+') {
            $result.PowerShell7Compatible = $false
            $result.Issues += [PSCustomObject]@{
                Type = 'Workflow'
                Description = 'PowerShell workflows are not supported in PowerShell 7'
                Recommendation = 'Convert workflow to functions or use PowerShell 5.1'
                Severity = 'High'
            }
        }

        # Check for ISE specific features
        if ($content -match '\$psISE' -or $content -match 'Microsoft\.PowerShell\.Host\.ISE') {
            $result.PowerShell7Compatible = $false
            $result.Issues += [PSCustomObject]@{
                Type = 'ISE'
                Description = 'PowerShell ISE is not available in PowerShell 7'
                Recommendation = 'Use Visual Studio Code or other editors for PowerShell 7'
                Severity = 'Medium'
            }
        }

        # Check for Windows PowerShell specific variables
        if ($content -match '\$PSVersionTable\.PSEdition\s*-eq\s*[''"]Desktop[''"]') {
            $result.PowerShell7Compatible = $false
            $result.Issues += [PSCustomObject]@{
                Type = 'WindowsPSVariable'
                Description = "Usage of PSVersionTable.PSEdition with Desktop edition check"
                Recommendation = 'Use cross-version compatible checks'
                Severity = 'Medium'
            }
        }

        return $result

    } catch {
        $result.Issues += [PSCustomObject]@{
            Type = 'Error'
            Description = "Error analyzing file: $($_.Exception.Message)"
            Recommendation = 'Check file permissions and format'
            Severity = 'High'
        }
        $result.PowerShell51Compatible = $false
        $result.PowerShell7Compatible = $false
        return $result
    }
}