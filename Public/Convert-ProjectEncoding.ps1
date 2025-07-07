function Convert-ProjectEncoding {
    <#
    .SYNOPSIS
    Converts encoding for all source files in a project directory with comprehensive safety features.

    .DESCRIPTION
    Recursively converts encoding for PowerShell, C#, and other source code files in a project directory.
    Includes comprehensive safety features: WhatIf support, automatic backups, rollback protection,
    and detailed reporting. Designed specifically for development projects with intelligent file type detection.

    .PARAMETER Path
    Path to the project directory to process.

    .PARAMETER ProjectType
    Type of project to process. Determines which file extensions are included.
    Valid values: 'PowerShell', 'CSharp', 'Mixed', 'All', 'Custom'

    .PARAMETER CustomExtensions
    Custom file extensions to process when ProjectType is 'Custom'.
    Example: @('*.ps1', '*.psm1', '*.cs', '*.vb')

    .PARAMETER SourceEncoding
    Expected source encoding of files. Default is 'UTF8BOM'.

    .PARAMETER TargetEncoding
    Target encoding for conversion.
    Default is 'UTF8BOM' for PowerShell projects (PS 5.1 compatibility), 'UTF8' for others.

    .PARAMETER ExcludeDirectories
    Directory names to exclude from processing (e.g., '.git', 'bin', 'obj').

    .PARAMETER CreateBackups
    Create backup files before conversion for additional safety.

    .PARAMETER BackupDirectory
    Directory to store backup files. If not specified, backups are created alongside original files.

    .PARAMETER Force
    Convert files even when their detected encoding doesn't match SourceEncoding.

    .PARAMETER NoRollbackOnMismatch
    Skip rolling back changes when content verification fails.

    .PARAMETER PassThru
    Return detailed results for each processed file.

    .EXAMPLE
    Convert-ProjectEncoding -Path 'C:\MyProject' -ProjectType PowerShell -WhatIf
    Preview encoding conversion for a PowerShell project (will use UTF8BOM by default).

    .EXAMPLE
    Convert-ProjectEncoding -Path 'C:\MyProject' -ProjectType Mixed -SourceEncoding ASCII -TargetEncoding UTF8BOM -CreateBackups
    Convert a mixed project from ASCII to UTF8BOM with backups (PowerShell-safe encoding).

    .EXAMPLE
    Convert-ProjectEncoding -Path 'C:\MyProject' -ProjectType CSharp -TargetEncoding UTF8 -PassThru
    Convert a C# project to UTF8 without BOM and return detailed results.

    .NOTES
    File type mappings:
    - PowerShell: *.ps1, *.psm1, *.psd1, *.ps1xml
    - CSharp: *.cs, *.csx, *.csproj, *.sln, *.config, *.json, *.xml
    - Mixed: Combination of PowerShell and CSharp
    - All: Common source code extensions including JS, Python, etc.

    PowerShell Encoding Recommendations:
    - UTF8BOM is recommended for PowerShell files to ensure PS 5.1 compatibility
    - UTF8 without BOM can cause PS 5.1 to misinterpret files as ASCII
    - This can lead to broken special characters and module loading issues
    - UTF8BOM ensures proper encoding detection across all PowerShell versions
    #>
    [CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'ProjectType')]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(ParameterSetName = 'ProjectType')]
        [ValidateSet('PowerShell', 'CSharp', 'Mixed', 'All')]
        [string] $ProjectType = 'Mixed',

        [Parameter(ParameterSetName = 'Custom', Mandatory)]
        [string[]] $CustomExtensions,

        [ValidateSet('Ascii', 'BigEndianUnicode', 'Unicode', 'UTF7', 'UTF8', 'UTF8BOM', 'UTF32', 'Default', 'OEM')]
        [string] $SourceEncoding = 'UTF8BOM',

        [ValidateSet('Ascii', 'BigEndianUnicode', 'Unicode', 'UTF7', 'UTF8', 'UTF8BOM', 'UTF32', 'Default', 'OEM')]
        [string] $TargetEncoding,

        [string[]] $ExcludeDirectories = @('.git', '.vs', 'bin', 'obj', 'packages', 'node_modules', '.vscode'),

        [switch] $CreateBackups,

        [string] $BackupDirectory,

        [switch] $Force,
        [switch] $NoRollbackOnMismatch,
        [switch] $PassThru
    )

    # Validate path
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Project path '$Path' not found or is not a directory"
    }

    # Define file extension mappings
    $extensionMappings = @{
        'PowerShell' = @('*.ps1', '*.psm1', '*.psd1', '*.ps1xml')
        'CSharp'     = @('*.cs', '*.csx', '*.csproj', '*.sln', '*.config', '*.json', '*.xml', '*.resx')
        'Mixed'      = @('*.ps1', '*.psm1', '*.psd1', '*.ps1xml', '*.cs', '*.csx', '*.csproj', '*.sln', '*.config', '*.json', '*.xml')
        'All'        = @('*.ps1', '*.psm1', '*.psd1', '*.ps1xml', '*.cs', '*.csx', '*.csproj', '*.sln', '*.config', '*.json', '*.xml', '*.js', '*.ts', '*.py', '*.rb', '*.java', '*.cpp', '*.h', '*.hpp', '*.sql', '*.md', '*.txt', '*.yaml', '*.yml')
    }

    # Determine file patterns to process
    if ($PSCmdlet.ParameterSetName -eq 'Custom') {
        $filePatterns = $CustomExtensions
    } else {
        $filePatterns = $extensionMappings[$ProjectType]
    }

    Write-Verbose "Processing project type: $ProjectType with patterns: $($filePatterns -join ', ')"

    # Set default TargetEncoding based on project type if not specified
    if (-not $PSBoundParameters.ContainsKey('TargetEncoding')) {
        switch ($ProjectType) {
            'PowerShell' { $TargetEncoding = 'UTF8BOM' }
            'Mixed' { $TargetEncoding = 'UTF8BOM' }  # Mixed likely contains PowerShell files
            default { $TargetEncoding = 'UTF8' }
        }
        Write-Verbose "Using default TargetEncoding '$TargetEncoding' for project type '$ProjectType'"
    }

    # Prepare backup directory if specified
    if ($CreateBackups -and $BackupDirectory) {
        if (-not (Test-Path -LiteralPath $BackupDirectory)) {
            New-Item -Path $BackupDirectory -ItemType Directory -Force | Out-Null
            Write-Verbose "Created backup directory: $BackupDirectory"
        }
    }

    # Resolve encodings
    $source = Resolve-Encoding -Name $SourceEncoding
    $target = Resolve-Encoding -Name $TargetEncoding

    # Collect all files to process
    $allFiles = @()

    foreach ($pattern in $filePatterns) {
        $params = @{
            Path    = $Path
            Filter  = $pattern
            File    = $true
            Recurse = $true
        }

        $files = Get-ChildItem @params | Where-Object {
            $file = $_
            $excluded = $false

            foreach ($excludeDir in $ExcludeDirectories) {
                if ($file.DirectoryName -like "*\$excludeDir" -or $file.DirectoryName -like "*\$excludeDir\*") {
                    $excluded = $true
                    break
                }
            }

            -not $excluded
        }

        $allFiles += $files
    }

    # Remove duplicates (files matching multiple patterns)
    $uniqueFiles = $allFiles | Sort-Object FullName | Get-Unique -AsString

    Write-Host "Found $($uniqueFiles.Count) files to process" -ForegroundColor Green

    if ($uniqueFiles.Count -eq 0) {
        Write-Warning "No files found matching the specified criteria"
        return
    }

    # Process files
    $results = @()
    $converted = 0
    $skipped = 0
    $errors = 0

    foreach ($file in $uniqueFiles) {
        try {
            $convertParams = @{
                FilePath             = $file.FullName
                SourceEncoding       = $source
                TargetEncoding       = $target
                Force                = $Force
                NoRollbackOnMismatch = $NoRollbackOnMismatch
                WhatIf               = $WhatIfPreference
            }

            if ($CreateBackups) {
                $convertParams.CreateBackup = $true
            }

            $result = Convert-FileEncodingSingle @convertParams

            if ($result) {
                $results += $result

                switch ($result.Status) {
                    'Converted' { $converted++ }
                    'Skipped' { $skipped++ }
                    'Error' { $errors++ }
                    'Failed' { $errors++ }
                }

                # Move backup to specified directory if requested
                if ($CreateBackups -and $BackupDirectory -and $result.BackupPath -and (Test-Path $result.BackupPath)) {
                    $relativePath = [System.IO.Path]::GetRelativePath($Path, $file.FullName)
                    $backupTargetPath = Join-Path $BackupDirectory $relativePath
                    $backupTargetDir = Split-Path $backupTargetPath -Parent

                    if (-not (Test-Path $backupTargetDir)) {
                        New-Item -Path $backupTargetDir -ItemType Directory -Force | Out-Null
                    }

                    Move-Item -Path $result.BackupPath -Destination $backupTargetPath -Force
                    $result.BackupPath = $backupTargetPath
                }
            }
        } catch {
            Write-Warning "Unexpected error processing $($file.FullName): $_"
            $errors++
        }
    }

    # Summary report
    $summary = @{
        TotalFiles     = $uniqueFiles.Count
        Converted      = $converted
        Skipped        = $skipped
        Errors         = $errors
        SourceEncoding = $SourceEncoding
        TargetEncoding = $TargetEncoding
        ProjectPath    = $Path
        ProjectType    = if ($PSCmdlet.ParameterSetName -eq 'Custom') { "Custom ($($CustomExtensions -join ', '))" } else { $ProjectType }
    }

    Write-Host "`nConversion Summary:" -ForegroundColor Cyan
    Write-Host "  Total files processed: $($summary.TotalFiles)" -ForegroundColor White
    Write-Host "  Successfully converted: $($summary.Converted)" -ForegroundColor Green
    Write-Host "  Skipped: $($summary.Skipped)" -ForegroundColor Yellow
    Write-Host "  Errors: $($summary.Errors)" -ForegroundColor Red
    Write-Host "  Encoding: $($summary.SourceEncoding) → $($summary.TargetEncoding)" -ForegroundColor White

    if ($PassThru) {
        [PSCustomObject]@{
            Summary = $summary
            Results = $results
        }
    }
}
