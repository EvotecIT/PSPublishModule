# Get public and private function definition files.
$Public  = [string[]]@(Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, 'Public', '*.ps1')) -ErrorAction SilentlyContinue -Recurse | Select-Object -ExpandProperty FullName)
$Private = [string[]]@(Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, 'Private', '*.ps1')) -ErrorAction SilentlyContinue -Recurse | Select-Object -ExpandProperty FullName)
$Classes = [string[]]@(Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, 'Classes', '*.ps1')) -ErrorAction SilentlyContinue -Recurse | Select-Object -ExpandProperty FullName)
$Enums   = [string[]]@(Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, 'Enums', '*.ps1')) -ErrorAction SilentlyContinue -Recurse | Select-Object -ExpandProperty FullName)
[Array]::Sort($Public, [StringComparer]::Ordinal)
[Array]::Sort($Private, [StringComparer]::Ordinal)
[Array]::Sort($Classes, [StringComparer]::Ordinal)
[Array]::Sort($Enums, [StringComparer]::Ordinal)

$FoundErrors = @(
    # Dot source the files (Classes/Enums first).
    foreach ($Import in @($Enums + $Classes + $Private + $Public)) {
        try {
            . $Import
        } catch {
            Write-Error -Message "Failed to import functions from ${Import}: $_"
            $true
        }
    }
)

if ($FoundErrors.Count -gt 0) {
    $ModuleName = (Get-ChildItem -Path ([IO.Path]::Combine({{ModuleRootExpression}}, '*.psd1'))).BaseName
    Write-Warning "Importing module $ModuleName failed. Fix errors before continuing."
    break
}
