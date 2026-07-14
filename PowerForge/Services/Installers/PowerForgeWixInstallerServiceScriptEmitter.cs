using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

internal static class PowerForgeWixInstallerServiceScriptEmitter
{
    private static readonly XNamespace WixNamespace = "http://wixtoolset.org/schemas/v4/wxs";

    internal static bool RequiresUtilExtension(PowerForgeInstallerDefinition definition)
        => definition.Components
            .OfType<PowerForgeInstallerServiceComponent>()
            .Any(service => service.ScriptInstall is not null);

    internal static IEnumerable<XElement> EmitScriptInstallActions(PowerForgeInstallerDefinition definition)
    {
        var actions = new List<XElement>();
        var sequence = new XElement(WixNamespace + "InstallExecuteSequence");
        var upgradePreparationActions = new List<UpgradePreparationAction>();
        foreach (var service in definition.Components.OfType<PowerForgeInstallerServiceComponent>())
        {
            if (service.ScriptInstall is null)
                continue;

            EmitServiceActions(service, actions, sequence, upgradePreparationActions);
        }

        AddUpgradePreparationSequenceRows(
            sequence,
            upgradePreparationActions,
            definition.Product.MajorUpgradeSchedule);

        foreach (var action in actions)
            yield return action;
        if (sequence.HasElements)
            yield return sequence;
    }

    internal static IEnumerable<string> GetGeneratedActionIds(PowerForgeInstallerServiceComponent service)
    {
        if (service.ScriptInstall is null)
            yield break;

        var ids = BuildIds(service.Id);
        yield return ids.BackupImagePathId;
        yield return ids.SetBackupCommandId;
        yield return ids.SetStopServiceId;
        yield return ids.StopServiceId;
        yield return ids.InstallServiceId;
        yield return ids.SetInstallStandardId;
        yield return ids.SetInstallUpgradeId;
        yield return ids.UninstallServiceId;
        yield return ids.SetUninstallServiceId;
    }

    private static void EmitServiceActions(
        PowerForgeInstallerServiceComponent service,
        ICollection<XElement> actions,
        XElement sequence,
        ICollection<UpgradePreparationAction> upgradePreparationActions)
    {
        var script = service.ScriptInstall!;
        var ids = BuildIds(service.Id);
        var resolvedBackupPath = ResolveBackupPath(service, script);
        bool hasUninstallCommand = !string.IsNullOrWhiteSpace(script.UninstallCommand);
        string? serviceExistsProperty = string.IsNullOrWhiteSpace(script.UpgradeCommand) && !hasUninstallCommand
            ? null
            : ids.ServiceExistsPropertyId;
        var existingServiceUpgradeSignal = !string.IsNullOrWhiteSpace(serviceExistsProperty) &&
                                           CanUseExistingServiceUpgradeSignal(service)
            ? serviceExistsProperty
            : null;
        var upgradeSignal = string.IsNullOrWhiteSpace(existingServiceUpgradeSignal)
            ? "WIX_UPGRADE_DETECTED"
            : "WIX_UPGRADE_DETECTED OR " + existingServiceUpgradeSignal;
        var upgradeCondition = CombineConditions(script.Condition, upgradeSignal, "NOT REMOVE=\"ALL\"");
        var standardCondition = string.IsNullOrWhiteSpace(script.UpgradeCommand)
            ? script.Condition
            : CombineConditions(script.Condition, "NOT (" + upgradeSignal + ")", "NOT REMOVE=\"ALL\"");
        string uninstallCondition = !hasUninstallCommand || string.IsNullOrWhiteSpace(serviceExistsProperty)
            ? script.UninstallCondition
            : CombineConditions(script.UninstallCondition, serviceExistsProperty!);

        if (!string.IsNullOrWhiteSpace(serviceExistsProperty))
        {
            actions.Add(CreateExistingServiceSearch(serviceExistsProperty!, service));
        }

        if (script.BackupExistingImagePath)
        {
            actions.Add(CreateSetQuietExecCommand(ids.SetBackupCommandId, BuildBackupCommand(service, resolvedBackupPath)));
            actions.Add(CreateQuietExecAction(ids.BackupImagePathId, execute: "immediate"));
        }

        if (script.StopServiceForUpgrade)
        {
            actions.Add(CreateQuietExecAction(ids.StopServiceId, execute: "immediate"));
            actions.Add(CreateSetQuietExecCommand(ids.SetStopServiceId, BuildStopCommand(service, script)));
        }

        actions.Add(CreateQuietExecAction(ids.InstallServiceId, execute: "deferred", hideTarget: true));

        if (!string.IsNullOrWhiteSpace(script.UpgradeCommand))
        {
            actions.Add(CreateSetInstallCommand(ids.SetInstallUpgradeId, ids.InstallServiceId, script.UpgradeCommand!));
        }

        if (hasUninstallCommand)
        {
            actions.Add(CreateQuietExecAction(ids.UninstallServiceId, execute: "deferred", hideTarget: true));
            actions.Add(CreateSetInstallCommand(ids.SetUninstallServiceId, ids.UninstallServiceId, script.UninstallCommand!));
        }

        actions.Add(CreateSetInstallCommand(ids.SetInstallStandardId, ids.InstallServiceId, script.Command));
        AddSequenceRows(
            sequence,
            ids,
            script,
            standardCondition,
            upgradeCondition,
            uninstallCondition,
            upgradePreparationActions);
    }

    private static void AddSequenceRows(
        XElement sequence,
        ServiceScriptActionIds ids,
        PowerForgeInstallerServiceScriptInstall script,
        string standardCondition,
        string upgradeCondition,
        string uninstallCondition,
        ICollection<UpgradePreparationAction> upgradePreparationActions)
    {
        if (script.BackupExistingImagePath)
        {
            upgradePreparationActions.Add(new UpgradePreparationAction(ids.SetBackupCommandId, upgradeCondition));
            upgradePreparationActions.Add(new UpgradePreparationAction(ids.BackupImagePathId, upgradeCondition));
        }

        if (script.StopServiceForUpgrade)
        {
            upgradePreparationActions.Add(new UpgradePreparationAction(ids.SetStopServiceId, upgradeCondition));
            upgradePreparationActions.Add(new UpgradePreparationAction(ids.StopServiceId, upgradeCondition));
        }

        if (!string.IsNullOrWhiteSpace(script.UpgradeCommand))
        {
            sequence.Add(new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", ids.SetInstallUpgradeId),
                new XAttribute("Before", ids.InstallServiceId),
                new XAttribute("Condition", upgradeCondition)));
        }

        if (!string.IsNullOrWhiteSpace(script.UninstallCommand))
        {
            sequence.Add(new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", ids.SetUninstallServiceId),
                new XAttribute("Before", ids.UninstallServiceId),
                new XAttribute("Condition", uninstallCondition)));
            sequence.Add(new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", ids.UninstallServiceId),
                new XAttribute("Before", "RemoveFiles"),
                new XAttribute("Condition", uninstallCondition)));
        }

        sequence.Add(new XElement(
            WixNamespace + "Custom",
            new XAttribute("Action", ids.SetInstallStandardId),
            new XAttribute("Before", ids.InstallServiceId),
            new XAttribute("Condition", standardCondition)));
        sequence.Add(new XElement(
            WixNamespace + "Custom",
            new XAttribute("Action", ids.InstallServiceId),
            new XAttribute("Before", "InstallFinalize"),
            new XAttribute("Condition", script.Condition)));
    }

    private static void AddUpgradePreparationSequenceRows(
        XElement sequence,
        IReadOnlyList<UpgradePreparationAction> actions,
        PowerForgeInstallerMajorUpgradeSchedule schedule)
    {
        if (actions.Count == 0)
            return;

        string anchor = schedule is PowerForgeInstallerMajorUpgradeSchedule.AfterInstallExecute or
            PowerForgeInstallerMajorUpgradeSchedule.AfterInstallExecuteAgain or
            PowerForgeInstallerMajorUpgradeSchedule.AfterInstallFinalize
                ? "InstallFiles"
                : "RemoveExistingProducts";

        for (int index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            string before = index + 1 < actions.Count
                ? actions[index + 1].ActionId
                : anchor;
            sequence.Add(new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", action.ActionId),
                new XAttribute("Before", before),
                new XAttribute("Condition", action.Condition)));
        }
    }

    private sealed class UpgradePreparationAction
    {
        public UpgradePreparationAction(string actionId, string condition)
        {
            ActionId = actionId;
            Condition = condition;
        }

        public string ActionId { get; }

        public string Condition { get; }
    }

    private static XElement CreateQuietExecAction(string id, string execute, bool hideTarget = false)
    {
        var element = new XElement(
            WixNamespace + "CustomAction",
            new XAttribute("Id", id),
            new XAttribute("BinaryRef", "Wix4UtilCA_$(sys.BUILDARCHSHORT)"),
            new XAttribute("DllEntry", "WixQuietExec"),
            new XAttribute("Execute", execute),
            new XAttribute("Return", "check"));
        if (string.Equals(execute, "deferred", StringComparison.OrdinalIgnoreCase))
            element.Add(new XAttribute("Impersonate", "no"));
        if (hideTarget)
            element.Add(new XAttribute("HideTarget", "yes"));
        return element;
    }

    private static XElement CreateExistingServiceSearch(string propertyId, PowerForgeInstallerServiceComponent service)
        => new(
            WixNamespace + "Property",
            new XAttribute("Id", propertyId),
            new XAttribute("Secure", "yes"),
            new XElement(
                WixNamespace + "RegistrySearch",
                new XAttribute("Id", propertyId + "_SEARCH"),
                new XAttribute("Root", "HKLM"),
                new XAttribute("Key", "SYSTEM\\CurrentControlSet\\Services\\" + service.ServiceName),
                new XAttribute("Name", "ImagePath"),
                new XAttribute("Type", "raw")));

    private static XElement CreateSetInstallCommand(string id, string actionId, string command)
        => new(
            WixNamespace + "CustomAction",
            new XAttribute("Id", id),
            new XAttribute("Property", actionId),
            new XAttribute("Value", command),
            new XAttribute("Execute", "immediate"));

    private static XElement CreateSetQuietExecCommand(string id, string command)
        => CreateSetInstallCommand(id, "WixQuietExecCmdLine", command);

    private static string BuildBackupCommand(
        PowerForgeInstallerServiceComponent service,
        string backupPath)
    {
        string serviceName = EscapeCmdSetValue(service.ServiceName);
        string backup = EscapeCmdSetValue(backupPath);
        string command = "$svc=$env:PF_SERVICE; $backup=$env:PF_BACKUP; $key=Join-Path 'Registry::HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services' $svc; $p=(Get-ItemProperty -LiteralPath $key -Name ImagePath -ErrorAction SilentlyContinue).ImagePath; if ($null -ne $p) { [System.IO.File]::WriteAllText($backup, [string]$p) } elseif (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }";
        return "\"[%ComSpec]\" /c set \"PF_SERVICE=" +
               serviceName +
               "\" && set \"PF_BACKUP=" +
               backup +
               "\" && powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"" +
               EscapeCommandDoubleQuoted(command) +
               "\"";
    }

    private static string BuildStopCommand(
        PowerForgeInstallerServiceComponent service,
        PowerForgeInstallerServiceScriptInstall script)
    {
        var command = "\"[%ComSpec]\" /c sc.exe stop \"" + service.ServiceName + "\" >nul 2>nul";
        if (script.StopDelaySeconds > 0)
        {
            var pingCount = script.StopDelaySeconds + 1;
            command += " & ping 127.0.0.1 -n " + pingCount.ToString(CultureInfo.InvariantCulture) + " >nul";
        }

        return command + " & exit /b 0";
    }

    private static ServiceScriptActionIds BuildIds(string serviceComponentId)
    {
        var prefix = serviceComponentId.Length <= 32
            ? serviceComponentId
            : serviceComponentId.Substring(0, 32) + "_" + HashId(serviceComponentId);
        return new ServiceScriptActionIds(
            BuildActionId(prefix, "BackupImagePath"),
            BuildActionId(prefix, "SetBackupCommand"),
            BuildActionId(prefix, "SetStopService"),
            BuildActionId(prefix, "StopService"),
            BuildActionId(prefix, "InstallService"),
            BuildActionId(prefix, "SetInstallService"),
            BuildActionId(prefix, "SetInstallServiceUpgrade"),
            BuildActionId(prefix, "UninstallService"),
            BuildActionId(prefix, "SetUninstallService"),
            "PF_" + HashId(serviceComponentId).ToUpperInvariant() + "_SERVICE_EXISTS");
    }

    private static string BuildActionId(string prefix, string suffix)
        => prefix + "." + suffix;

    private static string ResolveBackupPath(
        PowerForgeInstallerServiceComponent service,
        PowerForgeInstallerServiceScriptInstall script)
        => ReplaceToken(
            ReplaceToken(script.BackupPath, "{serviceId}", service.Id),
            "{serviceName}",
            service.ServiceName);

    private static string ReplaceToken(string value, string token, string replacement)
        => Regex.Replace(
            value,
            Regex.Escape(token),
            _ => replacement,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static string EscapeCommandDoubleQuoted(string value)
        => (value ?? string.Empty).Replace("\"", "\\\"");

    private static string EscapePowerShellSingleQuoted(string value)
        => (value ?? string.Empty).Replace("'", "''");

    private static string EscapeCmdSetValue(string value)
        => (value ?? string.Empty).Replace("\"", string.Empty);

    private static string CombineConditions(params string[] conditions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = conditions
            .Where(condition => !string.IsNullOrWhiteSpace(condition))
            .Select(condition => condition.Trim())
            .Where(condition => seen.Add(condition))
            .Select(condition => "(" + condition + ")")
            .ToArray();
        return string.Join(" AND ", parts);
    }

    private static bool CanUseExistingServiceUpgradeSignal(PowerForgeInstallerServiceComponent service)
        => service.ScriptInstall?.SuppressServiceControl == true ||
           !RemovesServiceDuringInstall(service.ControlRemove);

    private static bool RemovesServiceDuringInstall(string? controlRemove)
        => string.Equals(controlRemove, "install", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(controlRemove, "both", StringComparison.OrdinalIgnoreCase);

    private static string HashId(string value)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToString(bytes, 0, 5).Replace("-", string.Empty);
    }

    private sealed class ServiceScriptActionIds
    {
        internal ServiceScriptActionIds(
            string backupImagePathId,
            string setBackupCommandId,
            string setStopServiceId,
            string stopServiceId,
            string installServiceId,
            string setInstallStandardId,
            string setInstallUpgradeId,
            string uninstallServiceId,
            string setUninstallServiceId,
            string serviceExistsPropertyId)
        {
            BackupImagePathId = backupImagePathId;
            SetBackupCommandId = setBackupCommandId;
            SetStopServiceId = setStopServiceId;
            StopServiceId = stopServiceId;
            InstallServiceId = installServiceId;
            SetInstallStandardId = setInstallStandardId;
            SetInstallUpgradeId = setInstallUpgradeId;
            UninstallServiceId = uninstallServiceId;
            SetUninstallServiceId = setUninstallServiceId;
            ServiceExistsPropertyId = serviceExistsPropertyId;
        }

        internal string BackupImagePathId { get; }
        internal string SetBackupCommandId { get; }
        internal string SetStopServiceId { get; }
        internal string StopServiceId { get; }
        internal string InstallServiceId { get; }
        internal string SetInstallStandardId { get; }
        internal string SetInstallUpgradeId { get; }
        internal string UninstallServiceId { get; }
        internal string SetUninstallServiceId { get; }
        internal string ServiceExistsPropertyId { get; }
    }
}
