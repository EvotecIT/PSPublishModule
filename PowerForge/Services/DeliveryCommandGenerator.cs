using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Generates public PowerShell commands (Install-/Update-) used to unpack module Internals to a destination folder.
/// </summary>
public sealed class DeliveryCommandGenerator
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new generator using the provided logger.
    /// </summary>
    public DeliveryCommandGenerator(ILogger logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Generates Install-/Update- commands into the module staging folder based on <paramref name="delivery"/>.
    /// Returns the generated command names.
    /// </summary>
    public DeliveryGeneratedCommand[] Generate(string stagingPath, string moduleName, DeliveryOptionsConfiguration delivery)
    {
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("StagingPath is required.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("ModuleName is required.", nameof(moduleName));
        if (delivery is null) throw new ArgumentNullException(nameof(delivery));

        var output = new List<DeliveryGeneratedCommand>();

        if (!delivery.GenerateInstallCommand && !delivery.GenerateUpdateCommand)
            return Array.Empty<DeliveryGeneratedCommand>();

        var publicFolder = Path.Combine(Path.GetFullPath(stagingPath), "Public");
        Directory.CreateDirectory(publicFolder);

        var installName = NormalizeCommandName(delivery.InstallCommandName) ??
                          $"Install-{moduleName.Trim()}";
        installName = NormalizeCommandName(installName) ?? installName;

        if (delivery.GenerateInstallCommand)
        {
            output.AddRange(TryWriteInstall(publicFolder, installName, moduleName, delivery));
        }

        if (delivery.GenerateUpdateCommand)
        {
            var updateName = NormalizeCommandName(delivery.UpdateCommandName) ??
                             $"Update-{moduleName.Trim()}";
            updateName = NormalizeCommandName(updateName) ?? updateName;

            if (string.Equals(updateName, installName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn($"Delivery update command '{updateName}' matches install command; skipping update command generation.");
            }
            else
            {
                output.AddRange(TryWriteUpdate(publicFolder, updateName, installName, moduleName));
            }
        }

        return output.ToArray();
    }

    private IEnumerable<DeliveryGeneratedCommand> TryWriteInstall(
        string publicFolder,
        string installCommandName,
        string moduleName,
        DeliveryOptionsConfiguration delivery)
    {
        var scriptPath = Path.Combine(publicFolder, installCommandName + ".ps1");
        if (File.Exists(scriptPath))
        {
            _logger.Warn($"Delivery install command '{installCommandName}' already exists; skipping generation. Path: {scriptPath}");
            return Array.Empty<DeliveryGeneratedCommand>();
        }

        var script = BuildInstallScript(installCommandName, moduleName, delivery);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _logger.Verbose($"Generated delivery install command: {installCommandName} -> {scriptPath}");
        return new[] { new DeliveryGeneratedCommand(installCommandName, scriptPath) };
    }

    private IEnumerable<DeliveryGeneratedCommand> TryWriteUpdate(
        string publicFolder,
        string updateCommandName,
        string installCommandName,
        string moduleName)
    {
        var scriptPath = Path.Combine(publicFolder, updateCommandName + ".ps1");
        if (File.Exists(scriptPath))
        {
            _logger.Warn($"Delivery update command '{updateCommandName}' already exists; skipping generation. Path: {scriptPath}");
            return Array.Empty<DeliveryGeneratedCommand>();
        }

        var script = BuildUpdateScript(updateCommandName, installCommandName, moduleName);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _logger.Verbose($"Generated delivery update command: {updateCommandName} -> {scriptPath}");
        return new[] { new DeliveryGeneratedCommand(updateCommandName, scriptPath) };
    }

    private static string? NormalizeCommandName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string EscapeSingleQuotes(string value)
        => (value ?? string.Empty).Replace("'", "''");

    private static string BuildInstallScript(
        string commandName,
        string moduleName,
        DeliveryOptionsConfiguration delivery)
    {
        var internalsPath = string.IsNullOrWhiteSpace(delivery.InternalsPath) ? "Internals" : delivery.InternalsPath.Trim();
        var includeRootReadme = delivery.IncludeRootReadme ? "$true" : "$false";
        var includeRootChangelog = delivery.IncludeRootChangelog ? "$true" : "$false";
        var includeRootLicense = delivery.IncludeRootLicense ? "$true" : "$false";

        var escInternals = EscapeSingleQuotes(internalsPath);
        var escModule = EscapeSingleQuotes(moduleName);
        var escCommand = EscapeSingleQuotes(commandName);

        // Keep the script self-contained and compatible with Windows PowerShell 5.1.
        // NOTE: $IsWindows is not available on Desktop; use $env:OS instead.
        return $@"
function {escCommand} {{
    <#
    .SYNOPSIS
    Installs bundled module artefacts to a folder.

    .DESCRIPTION
    Copies files from the module's '{escInternals}' folder into a destination path.
    By default, existing files are preserved (OnExists=Merge) so local configuration is not overwritten.

    .PARAMETER Path
    Destination folder for extracted artefacts.

    .PARAMETER OnExists
    What to do when the destination folder already exists:
    - Merge (default): keep existing files; copy only missing files (use -Force to overwrite files)
    - Overwrite: delete the destination folder and recreate it
    - Skip: do nothing
    - Stop: throw an error

    .PARAMETER Force
    When OnExists=Merge, overwrites existing files.

    .PARAMETER ListOnly
    Shows planned copy actions without writing any changes.

    .PARAMETER Unblock
    On Windows, removes Zone.Identifier (best effort) from copied files.

    .EXAMPLE
    {escCommand} -Path 'C:\Tools' -Verbose
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Path,

        [ValidateSet('Merge', 'Overwrite', 'Skip', 'Stop')]
        [string] $OnExists = 'Merge',

        [switch] $Force,
        [switch] $ListOnly,
        [switch] $Unblock
    )

    $moduleBase = $null
    try {{ $moduleBase = $MyInvocation.MyCommand.Module.ModuleBase }} catch {{ $moduleBase = $null }}
    if ([string]::IsNullOrWhiteSpace($moduleBase)) {{
        throw ""[{escModule}] Unable to resolve module base path.""
    }}

    $internalsRel = '{escInternals}'
    $internalsRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($moduleBase, $internalsRel))
    if (-not (Test-Path -LiteralPath $internalsRoot)) {{
        throw ""[{escModule}] Internals folder not found: $internalsRoot""
    }}

    $dest = $Path
    if (-not [System.IO.Path]::IsPathRooted($dest)) {{
        $dest = [System.IO.Path]::Combine((Get-Location).Path, $dest)
    }}
    $dest = [System.IO.Path]::GetFullPath($dest)

    if (Test-Path -LiteralPath $dest) {{
        switch ($OnExists) {{
            'Skip' {{ return $dest }}
            'Stop' {{ throw ""[{escModule}] Destination already exists: $dest"" }}
            'Overwrite' {{ Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction Stop }}
            default {{ }}
        }}
    }}

    New-Item -ItemType Directory -Force -Path $dest | Out-Null

    $files = [System.IO.Directory]::GetFiles($internalsRoot, '*', [System.IO.SearchOption]::AllDirectories)
    if ($ListOnly) {{
        foreach ($file in $files) {{
            $rel = $file.Substring($internalsRoot.Length).TrimStart('\','/')
            $target = [System.IO.Path]::Combine($dest, $rel)
            $exists = Test-Path -LiteralPath $target
            $action = if ($exists) {{ if ($Force) {{ 'Overwrite' }} else {{ 'Keep' }} }} else {{ 'Copy' }}
            [pscustomobject]@{{ Source = $file; Destination = $target; Exists = $exists; Action = $action }}
        }}
        return
    }}

    if (-not $PSCmdlet.ShouldProcess($dest, ""Install artefacts from '$internalsRel'"")) {{ return }}

    foreach ($file in $files) {{
        $rel = $file.Substring($internalsRoot.Length).TrimStart('\','/')
        $target = [System.IO.Path]::Combine($dest, $rel)
        $targetDir = [System.IO.Path]::GetDirectoryName($target)
        if ($targetDir -and -not (Test-Path -LiteralPath $targetDir)) {{
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        }}

        if ((Test-Path -LiteralPath $target) -and (-not $Force)) {{
            continue
        }}

        Copy-Item -LiteralPath $file -Destination $target -Force

        if ($Unblock -and ($env:OS -eq 'Windows_NT')) {{
            try {{ Unblock-File -LiteralPath $target -ErrorAction SilentlyContinue }} catch {{ }}
        }}
    }}

    $includeRootReadme = {includeRootReadme}
    $includeRootChangelog = {includeRootChangelog}
    $includeRootLicense = {includeRootLicense}

    if ($includeRootReadme) {{
        try {{
            Get-ChildItem -LiteralPath $moduleBase -Filter 'README*' -File -ErrorAction SilentlyContinue | ForEach-Object {{
                $target = [System.IO.Path]::Combine($dest, $_.Name)
                if ((Test-Path -LiteralPath $target) -and (-not $Force)) {{ return }}
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
            }}
        }} catch {{ }}
    }}

    if ($includeRootChangelog) {{
        try {{
            Get-ChildItem -LiteralPath $moduleBase -Filter 'CHANGELOG*' -File -ErrorAction SilentlyContinue | ForEach-Object {{
                $target = [System.IO.Path]::Combine($dest, $_.Name)
                if ((Test-Path -LiteralPath $target) -and (-not $Force)) {{ return }}
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
            }}
        }} catch {{ }}
    }}

    if ($includeRootLicense) {{
        try {{
            $lic = Get-ChildItem -LiteralPath $moduleBase -Filter 'LICENSE*' -File -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($lic) {{
                $target = [System.IO.Path]::Combine($dest, 'license.txt')
                if (-not ((Test-Path -LiteralPath $target) -and (-not $Force))) {{
                    Copy-Item -LiteralPath $lic.FullName -Destination $target -Force
                }}
            }}
        }} catch {{ }}
    }}

    return $dest
}}
";
    }

    private static string BuildUpdateScript(string updateCommandName, string installCommandName, string moduleName)
    {
        var escModule = EscapeSingleQuotes(moduleName);
        var escUpdate = EscapeSingleQuotes(updateCommandName);
        var escInstall = EscapeSingleQuotes(installCommandName);

        return $@"
function {escUpdate} {{
    <#
    .SYNOPSIS
    Updates bundled module artefacts in a folder.

    .DESCRIPTION
    Delegates to {escInstall}. Re-running the install command is typically enough to update new files while keeping existing configuration.

    .EXAMPLE
    {escUpdate} -Path 'C:\Tools' -Verbose
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Path,

        [ValidateSet('Merge', 'Overwrite', 'Skip', 'Stop')]
        [string] $OnExists = 'Merge',

        [switch] $Force,
        [switch] $ListOnly,
        [switch] $Unblock
    )

    if (-not (Get-Command -Name '{escInstall}' -ErrorAction SilentlyContinue)) {{
        throw ""[{escModule}] Expected install command not found: {escInstall}""
    }}

    & '{escInstall}' @PSBoundParameters
}}
";
    }
}

/// <summary>
/// Represents a generated delivery command.
/// </summary>
public sealed class DeliveryGeneratedCommand
{
    /// <summary>Command name (Verb-Noun).</summary>
    public string Name { get; }

    /// <summary>Path to the generated script file.</summary>
    public string ScriptPath { get; }

    /// <summary>Creates a new instance.</summary>
    public DeliveryGeneratedCommand(string name, string scriptPath)
    {
        Name = name ?? string.Empty;
        ScriptPath = scriptPath ?? string.Empty;
    }
}
