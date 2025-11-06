#Get public and private function definition files.
$Public = @( Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, 'Public', '*.ps1')) -ErrorAction SilentlyContinue -Recurse )
$Private = @( Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, 'Private', '*.ps1')) -ErrorAction SilentlyContinue -Recurse )
$Classes = @( Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, 'Classes', '*.ps1')) -ErrorAction SilentlyContinue -Recurse )
$Enums = @( Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, 'Enums', '*.ps1')) -ErrorAction SilentlyContinue -Recurse )

$AssemblyFolders = Get-ChildItem -Path ([IO.Path]::Combine($PSScriptRoot, 'Lib')) -Directory -ErrorAction SilentlyContinue
$Assembly = @(
    if ($AssemblyFolders.BaseName -contains 'Standard') {
        @( Get-ChildItem -Path ([IO.Path]::Combine($LibPath, 'Standard', '*.dll')) -ErrorAction SilentlyContinue -Recurse)
    }
    if ($PSEdition -eq 'Core') {
        @( Get-ChildItem -Path ([IO.Path]::Combine($LibPath, 'Core', '*.dll')) -ErrorAction SilentlyContinue -Recurse )
    } else {
        @( Get-ChildItem -Path ([IO.Path]::Combine($LibPath, 'Default', '*.dll')) -ErrorAction SilentlyContinue -Recurse )
    }
)
$FoundErrors = @(
    foreach ($Import in @($Assembly)) {
        try {
            Add-Type -Path $Import.Fullname -ErrorAction Stop
        } catch [System.Reflection.ReflectionTypeLoadException] {
            Write-Warning "Processing $($Import.Name) Exception: $($_.Exception.Message)"
            $LoaderExceptions = $($_.Exception.LoaderExceptions) | Sort-Object -Unique
            foreach ($E in $LoaderExceptions) {
                Write-Warning "Processing $($Import.Name) LoaderExceptions: $($E.Message)"
            }
            $true
            #Write-Error -Message "StackTrace: $($_.Exception.StackTrace)"
        } catch {
            Write-Warning "Processing $($Import.Name) Exception: $($_.Exception.Message)"
            $LoaderExceptions = $($_.Exception.LoaderExceptions) | Sort-Object -Unique
            foreach ($E in $LoaderExceptions) {
                Write-Warning "Processing $($Import.Name) LoaderExceptions: $($E.Message)"
            }
            $true
            #Write-Error -Message "StackTrace: $($_.Exception.StackTrace)"
        }
    }
    #Dot source the files
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

Export-ModuleMember -Function '*' -Alias '*' -Cmdlet '*'
