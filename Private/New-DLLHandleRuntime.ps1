function New-DLLHandleRuntime {
    [CmdletBinding()]
    param(
        [alias('NETHandleRuntimes')][bool] $HandleRuntimes
    )
    if ($HandleRuntimes) {
        $DataHandleRuntimes = @"
        if (`$IsWindows) {
            `$Arch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
            `$ArchFolder = switch (`$Arch) {
                'X64'   { 'win-x64' }
                'X86'   { 'win-x86' }
                'Arm64' { 'win-arm64' }
                'Arm'   { 'win-arm' }
                Default { 'win-x64' }
            }

            `$LibFolder = if (`$PSEdition -eq 'Core') { 'Core' } else { 'Default' }
            `$NativePath = Join-Path -Path `$PSScriptRoot -ChildPath "Lib\`$LibFolder\runtimes\`$ArchFolder\native"

            if ((Test-Path `$NativePath) -and (`$env:PATH -notlike "*`$NativePath*")   ) {
                # Write-Warning -Message "Adding `$NativePath to PATH"
                `$env:PATH = "`$NativePath;`$env:PATH"
            }
        }
"@
        $DataHandleRuntimes
    }
}

