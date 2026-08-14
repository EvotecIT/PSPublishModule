namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    internal static IEnumerable<string> BuildWindowsRuntimeArchitectureResolverLines(
        string architectureVariable,
        string architectureFolderVariable)
    {
        var fallbackFolderExpression = $"if ([IntPtr]::Size -eq 4) {{ 'win-x86' }} else {{ 'win-x64' }}";

        return new[]
        {
            $"    {architectureVariable} = [string][System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture",
            $"    if ([string]::IsNullOrWhiteSpace({architectureVariable})) {{",
            $"        {architectureVariable} = [string]$env:PROCESSOR_ARCHITECTURE",
            "    }",
            $"    {architectureFolderVariable} = switch ({architectureVariable}) {{",
            "        'X64'   { 'win-x64' }",
            "        'AMD64' { 'win-x64' }",
            "        'X86'   { 'win-x86' }",
            "        'I386'  { 'win-x86' }",
            "        'Arm64' { 'win-arm64' }",
            "        'Arm'   { 'win-arm' }",
            "        Default {",
            $"            if ([string]::IsNullOrWhiteSpace({architectureVariable})) {{",
            $"                {fallbackFolderExpression}",
            "            } else {",
            $"                Write-Warning -Message (\"Unknown Windows architecture '{{0}}'. Falling back to process-bitness native runtime probing.\" -f {architectureVariable})",
            $"                {fallbackFolderExpression}",
            "            }",
            "        }",
            "    }"
        };
    }
}
