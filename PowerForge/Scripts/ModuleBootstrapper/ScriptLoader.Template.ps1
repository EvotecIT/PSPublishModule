# Get public and private function definition files.
$Public  = @(Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, 'Public', '*.ps1')) -ErrorAction SilentlyContinue -Recurse)
$Private = @(Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, 'Private', '*.ps1')) -ErrorAction SilentlyContinue -Recurse)
$Classes = @(Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, 'Classes', '*.ps1')) -ErrorAction SilentlyContinue -Recurse)
$Enums   = @(Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, 'Enums', '*.ps1')) -ErrorAction SilentlyContinue -Recurse)

$FoundErrors = @(
    # Dot source the files (Classes/Enums first).
    foreach ($Import in @($Classes + $Enums + $Private + $Public)) {
        try {
            . $Import.Fullname
        } catch {
            Write-Error -Message "Failed to import functions from $($import.Fullname): $_"
            $true
        }
    }
)

if ($FoundErrors.Count -gt 0) {
    $ModuleName = (Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, '*.psd1'))).BaseName
    Write-Warning "Importing module $ModuleName failed. Fix errors before continuing."
    break
}
