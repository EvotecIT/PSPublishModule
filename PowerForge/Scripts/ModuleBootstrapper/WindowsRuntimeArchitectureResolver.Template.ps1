{{ArchitectureVariable}} = [string][System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
if ([string]::IsNullOrWhiteSpace({{ArchitectureVariable}})) {
    {{ArchitectureVariable}} = [string]$env:PROCESSOR_ARCHITECTURE
}
{{ArchitectureFolderVariable}} = switch ({{ArchitectureVariable}}) {
    'X64'   { 'win-x64' }
    'AMD64' { 'win-x64' }
    'X86'   { 'win-x86' }
    'I386'  { 'win-x86' }
    'Arm64' { 'win-arm64' }
    'Arm'   { 'win-arm' }
    Default {
        if ([string]::IsNullOrWhiteSpace({{ArchitectureVariable}})) {
            {{FallbackFolderExpression}}
        } else {
            Write-Warning -Message ("Unknown Windows architecture '{0}'. Falling back to process-bitness native runtime probing." -f {{ArchitectureVariable}})
            {{FallbackFolderExpression}}
        }
    }
}
