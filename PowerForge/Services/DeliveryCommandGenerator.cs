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

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CommandName"] = escCommand,
            ["ModuleName"] = escModule,
            ["InternalsPath"] = escInternals,
            ["IncludeRootReadme"] = includeRootReadme,
            ["IncludeRootChangelog"] = includeRootChangelog,
            ["IncludeRootLicense"] = includeRootLicense
        };

        return RenderDeliveryTemplate(
            "Install-Delivery.ps1",
            "Scripts/DeliveryCommands/Install-Delivery.Template.ps1",
            tokens);
    }

    private static string BuildUpdateScript(string updateCommandName, string installCommandName, string moduleName)
    {
        var escModule = EscapeSingleQuotes(moduleName);
        var escUpdate = EscapeSingleQuotes(updateCommandName);
        var escInstall = EscapeSingleQuotes(installCommandName);

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UpdateCommandName"] = escUpdate,
            ["InstallCommandName"] = escInstall,
            ["ModuleName"] = escModule
        };

        return RenderDeliveryTemplate(
            "Update-Delivery.ps1",
            "Scripts/DeliveryCommands/Update-Delivery.Template.ps1",
            tokens);
    }

    private static string RenderDeliveryTemplate(
        string templateName,
        string embeddedScriptPath,
        IReadOnlyDictionary<string, string> tokens)
    {
        var template = EmbeddedScripts.Load(embeddedScriptPath);
        return ScriptTemplateRenderer.Render(templateName, template, tokens);
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
