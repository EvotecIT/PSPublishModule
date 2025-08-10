function Get-ProjectCleanupPatterns {
    <#
    .SYNOPSIS
    Gets cleanup patterns for different project types.

    .DESCRIPTION
    Returns file and folder patterns to clean up based on project type,
    or processes custom patterns for the Custom project type.

    .PARAMETER ProjectType
    Type of project cleanup to get patterns for.

    .PARAMETER IncludePatterns
    Custom patterns when ProjectType is 'Custom'.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Build', 'Logs', 'Html', 'Temp', 'All', 'Custom')]
        [string] $ProjectType,

        [string[]] $IncludePatterns
    )

    # Define cleanup patterns for different project types
    $cleanupMappings = @{
        'Build' = @{
            Folders        = @('bin', 'obj', 'packages', '.vs', '.vscode', 'TestResults', 'BenchmarkDotNet.Artifacts', 'coverage', 'x64', 'x86', 'Debug', 'Release')
            Files          = @('*.pdb', '*.dll', '*.exe', '*.cache', '*.tlog', '*.lastbuildstate', '*.unsuccessfulbuild')
            ExcludeFolders = @('Ignore')
        }
        'Logs'  = @{
            Folders        = @('logs', 'log')
            Files          = @('*.log', '*.tmp', '*.temp', '*.trace', '*.etl')
            ExcludeFolders = @('Ignore')
        }
        'Html'  = @{
            Folders        = @()
            Files          = @('*.html', '*.htm')
            ExcludeFolders = @('Assets', 'Docs', 'Examples', 'Documentation', 'Help', 'Ignore')
        }
        'Temp'  = @{
            Folders        = @('temp*', 'tmp*', 'cache*', '.temp', '.tmp')
            Files          = @('*.tmp', '*.temp', '*.cache', '~*', 'thumbs.db', 'desktop.ini')
            ExcludeFolders = @('Ignore')
        }
        'All'   = @{
            Folders        = @('bin', 'obj', 'packages', '.vs', '.vscode', 'TestResults', 'BenchmarkDotNet.Artifacts', 'coverage', 'x64', 'x86', 'Debug', 'Release', 'logs', 'log', 'temp*', 'tmp*', 'cache*', '.temp', '.tmp')
            Files          = @('*.pdb', '*.dll', '*.exe', '*.cache', '*.tlog', '*.lastbuildstate', '*.unsuccessfulbuild', '*.log', '*.tmp', '*.temp', '*.trace', '*.etl', '*.html', '*.htm', '~*', 'thumbs.db', 'desktop.ini')
            ExcludeFolders = @('Assets', 'Docs', 'Examples', 'Documentation', 'Help', 'Ignore')
        }
    }

    if ($ProjectType -eq 'Custom') {
        $folderPatterns = [System.Collections.Generic.List[string]]::new()
        $filePatterns = [System.Collections.Generic.List[string]]::new()

        # Separate file and folder patterns
        foreach ($pattern in $IncludePatterns) {
            if ($pattern -like '*.*') {
                $filePatterns.Add($pattern)
            } else {
                $folderPatterns.Add($pattern)
            }
        }

        return @{
            Folders        = $folderPatterns.ToArray()
            Files          = $filePatterns.ToArray()
            ExcludeFolders = @()
        }
    } else {
        return $cleanupMappings[$ProjectType]
    }
}
