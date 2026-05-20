using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal static class ModuleRepositoryBootstrapScriptBuilder
{
    public static ModuleRepositoryBootstrapScriptPackage WritePackage(ModuleRepositoryBootstrapScriptOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("OutputDirectory is required.", nameof(options));

        var profiles = (options.Profiles ?? Array.Empty<ModuleRepositoryProfile>())
            .Where(static profile => profile is not null)
            .Select(ModuleRepositoryProfileStore.Normalize)
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (profiles.Length == 0)
            throw new ArgumentException("At least one module repository profile is required.", nameof(options));

        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        var scriptName = NormalizeFileName(options.ScriptName, "Initialize-PrivateGallery.ps1", nameof(options.ScriptName));
        var profileFileName = NormalizeFileName(options.ProfileFileName, "profiles.json", nameof(options.ProfileFileName));
        var scriptPath = Path.Combine(outputDirectory, scriptName);
        var profilePath = Path.Combine(outputDirectory, profileFileName);

        if (!options.Force)
        {
            if (File.Exists(scriptPath))
                throw new IOException($"File '{scriptPath}' already exists. Use Force to overwrite it.");
            if (File.Exists(profilePath))
                throw new IOException($"File '{profilePath}' already exists. Use Force to overwrite it.");
        }

        Directory.CreateDirectory(outputDirectory);
        new ModuleRepositoryProfileStore().WriteProfilesFile(profilePath, profiles);
        File.WriteAllText(scriptPath, BuildScript(profileFileName, profiles, options.InstallModules ?? Array.Empty<string>()), Encoding.UTF8);

        return new ModuleRepositoryBootstrapScriptPackage
        {
            OutputDirectory = outputDirectory,
            ScriptPath = scriptPath,
            ProfilePath = profilePath,
            ProfileNames = profiles.Select(static profile => profile.Name).ToArray(),
            InstallModules = NormalizeInstallModules(options.InstallModules).ToArray(),
            RecommendedCommand = BuildRecommendedCommand(scriptName, profiles),
            ContainsSecrets = false
        };
    }

    private static string NormalizeFileName(string? value, string defaultValue, string parameterName)
    {
        var fileName = string.IsNullOrWhiteSpace(value) ? defaultValue : value!.Trim();
        if (Path.IsPathRooted(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"{parameterName} must be a file name, not a rooted path or invalid file name.", parameterName);

        return fileName;
    }

    private static IEnumerable<string> NormalizeInstallModules(IEnumerable<string>? installModules)
        => (installModules ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase);

    private static string BuildRecommendedCommand(string scriptName, IReadOnlyList<ModuleRepositoryProfile> profiles)
    {
        var builder = new StringBuilder();
        builder.Append(".\\").Append(scriptName);
        if (profiles.Count == 1)
            builder.Append(" -ProfileName ").Append(QuotePowerShellString(profiles[0].Name));

        return builder.ToString();
    }

    private static string BuildScript(
        string profileFileName,
        IReadOnlyList<ModuleRepositoryProfile> profiles,
        IEnumerable<string> installModules)
    {
        var defaultProfileName = profiles.Count == 1 ? profiles[0].Name : string.Empty;
        var moduleArray = BuildPowerShellArray(NormalizeInstallModules(installModules));
        var builder = new StringBuilder();
        builder.AppendLine("#requires -Version 5.1");
        builder.AppendLine("<#");
        builder.AppendLine(".SYNOPSIS");
        builder.AppendLine("Initializes PSPublishModule private gallery access on a managed workstation.");
        builder.AppendLine();
        builder.AppendLine(".DESCRIPTION");
        builder.AppendLine("Imports the bundled non-secret profile JSON, installs requested prerequisites unless skipped,");
        builder.AppendLine("connects to the private gallery, and can install approved modules through the saved profile.");
        builder.AppendLine("Authentication remains owned by PSResourceGet and the Azure Artifacts Credential Provider.");
        builder.AppendLine("#>");
        builder.AppendLine("[CmdletBinding()]");
        builder.AppendLine("param(");
        builder.AppendLine("    [Parameter()]");
        builder.AppendLine("    [string] $ProfileName = " + QuotePowerShellString(defaultProfileName) + ",");
        builder.AppendLine();
        builder.AppendLine("    [Parameter()]");
        builder.AppendLine("    [string[]] $InstallModule = " + moduleArray + ",");
        builder.AppendLine();
        builder.AppendLine("    [Parameter()]");
        builder.AppendLine("    [switch] $SkipConnect,");
        builder.AppendLine();
        builder.AppendLine("    [Parameter()]");
        builder.AppendLine("    [switch] $SkipInstallPrerequisites");
        builder.AppendLine(")");
        builder.AppendLine();
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        builder.AppendLine("$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }");
        builder.AppendLine("$profilePath = Join-Path -Path $scriptRoot -ChildPath " + QuotePowerShellString(profileFileName));
        builder.AppendLine("if (-not (Get-Command -Name Initialize-ModuleRepository -ErrorAction SilentlyContinue)) {");
        builder.AppendLine("    Import-Module PSPublishModule -ErrorAction Stop");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {");
        builder.AppendLine("    throw \"Profile file '$profilePath' was not found.\"");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("$initializeArguments = @{");
        builder.AppendLine("    Path = $profilePath");
        builder.AppendLine("    Overwrite = $true");
        builder.AppendLine("}");
        builder.AppendLine("if (-not [string]::IsNullOrWhiteSpace($ProfileName)) {");
        builder.AppendLine("    $initializeArguments.ProfileName = $ProfileName");
        builder.AppendLine("}");
        builder.AppendLine("if (-not $SkipInstallPrerequisites) {");
        builder.AppendLine("    $initializeArguments.InstallPrerequisites = $true");
        builder.AppendLine("}");
        builder.AppendLine("if ($SkipConnect) {");
        builder.AppendLine("    $initializeArguments.SkipConnect = $true");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("$onboarding = Initialize-ModuleRepository @initializeArguments");
        builder.AppendLine("$onboardingItems = @($onboarding)");
        builder.AppendLine();
        builder.AppendLine("if ($InstallModule -and $InstallModule.Count -gt 0) {");
        builder.AppendLine("    $targetProfileName = if (-not [string]::IsNullOrWhiteSpace($ProfileName)) { $ProfileName } elseif ($onboardingItems.Count -eq 1) { $onboardingItems[0].ProfileName } else { $null }");
        builder.AppendLine("    if ([string]::IsNullOrWhiteSpace($targetProfileName)) {");
        builder.AppendLine("        throw 'ProfileName is required when installing modules from a bootstrap package that contains multiple profiles.'");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    Install-PrivateModule -ProfileName $targetProfileName -Name $InstallModule -InstallPrerequisites:((-not $SkipInstallPrerequisites) -and (-not $SkipConnect))");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("$onboarding");
        return builder.ToString();
    }

    private static string BuildPowerShellArray(IEnumerable<string> values)
    {
        var quoted = values.Select(QuotePowerShellString).ToArray();
        return quoted.Length == 0 ? "@()" : "@(" + string.Join(", ", quoted) + ")";
    }

    private static string QuotePowerShellString(string value)
        => "'" + (value ?? string.Empty).Replace("'", "''") + "'";
}
