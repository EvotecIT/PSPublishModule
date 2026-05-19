using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        foreach (var service in definition.Components.OfType<PowerForgeInstallerServiceComponent>())
        {
            if (service.ScriptInstall is null)
                continue;

            EmitServiceActions(service, actions, sequence);
        }

        foreach (var action in actions)
            yield return action;
        if (sequence.HasElements)
            yield return sequence;
    }

    private static void EmitServiceActions(
        PowerForgeInstallerServiceComponent service,
        ICollection<XElement> actions,
        XElement sequence)
    {
        var script = service.ScriptInstall!;
        var ids = BuildIds(service.Id);
        var upgradeCondition = "WIX_UPGRADE_DETECTED AND NOT REMOVE=\"ALL\"";
        var standardCondition = string.IsNullOrWhiteSpace(script.UpgradeCommand)
            ? script.Condition
            : "NOT WIX_UPGRADE_DETECTED AND NOT REMOVE=\"ALL\"";

        if (script.BackupExistingImagePath)
        {
            actions.Add(CreateQuietExecAction(ids.BackupImagePathId, execute: "immediate"));
            actions.Add(new XElement(
                WixNamespace + "SetProperty",
                new XAttribute("Id", "WixQuietExecCmdLine"),
                new XAttribute("Value", BuildBackupCommand(service, script)),
                new XAttribute("Before", ids.BackupImagePathId),
                new XAttribute("Sequence", "execute"),
                new XAttribute("Condition", upgradeCondition)));
        }

        if (script.StopServiceForUpgrade)
        {
            actions.Add(CreateQuietExecAction(ids.StopServiceId, execute: "immediate"));
            actions.Add(new XElement(
                WixNamespace + "CustomAction",
                new XAttribute("Id", ids.SetStopServiceId),
                new XAttribute("Property", "WixQuietExecCmdLine"),
                new XAttribute("Value", BuildStopCommand(service, script)),
                new XAttribute("Execute", "immediate")));
        }

        actions.Add(CreateQuietExecAction(ids.InstallServiceId, execute: "deferred", hideTarget: true));

        if (!string.IsNullOrWhiteSpace(script.UpgradeCommand))
        {
            actions.Add(CreateSetInstallCommand(ids.SetInstallUpgradeId, ids.InstallServiceId, script.UpgradeCommand!));
        }

        actions.Add(CreateSetInstallCommand(ids.SetInstallStandardId, ids.InstallServiceId, script.Command));
        AddSequenceRows(sequence, ids, script, standardCondition, upgradeCondition);
    }

    private static void AddSequenceRows(
        XElement sequence,
        ServiceScriptActionIds ids,
        PowerForgeInstallerServiceScriptInstall script,
        string standardCondition,
        string upgradeCondition)
    {
        if (script.BackupExistingImagePath)
        {
            sequence.Add(new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", ids.BackupImagePathId),
                new XAttribute("Before", "RemoveExistingProducts"),
                new XAttribute("Condition", upgradeCondition)));
        }

        if (script.StopServiceForUpgrade)
        {
            var stopSet = new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", ids.SetStopServiceId),
                new XAttribute("Condition", upgradeCondition));
            if (script.BackupExistingImagePath)
                stopSet.Add(new XAttribute("After", ids.BackupImagePathId));
            else
                stopSet.Add(new XAttribute("Before", "RemoveExistingProducts"));
            sequence.Add(stopSet);
            sequence.Add(new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", ids.StopServiceId),
                new XAttribute("After", ids.SetStopServiceId),
                new XAttribute("Condition", upgradeCondition)));
        }

        if (!string.IsNullOrWhiteSpace(script.UpgradeCommand))
        {
            sequence.Add(new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", ids.SetInstallUpgradeId),
                new XAttribute("Before", ids.InstallServiceId),
                new XAttribute("Condition", upgradeCondition)));
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

    private static XElement CreateSetInstallCommand(string id, string actionId, string command)
        => new(
            WixNamespace + "CustomAction",
            new XAttribute("Id", id),
            new XAttribute("Property", actionId),
            new XAttribute("Value", command),
            new XAttribute("Execute", "immediate"));

    private static string BuildBackupCommand(
        PowerForgeInstallerServiceComponent service,
        PowerForgeInstallerServiceScriptInstall script)
        => "\"[%ComSpec]\" /c reg query \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\" +
           service.ServiceName +
           "\" /v ImagePath > \"" +
           script.BackupPath +
           "\" 2>nul";

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

        return command;
    }

    private static ServiceScriptActionIds BuildIds(string serviceComponentId)
    {
        var prefix = serviceComponentId.Length <= 48
            ? serviceComponentId
            : serviceComponentId.Substring(0, 48);
        return new ServiceScriptActionIds(
            prefix + "BackupImagePath",
            prefix + "SetStopService",
            prefix + "StopService",
            prefix + "InstallService",
            prefix + "SetInstallService",
            prefix + "SetInstallServiceUpgrade");
    }

    private sealed class ServiceScriptActionIds
    {
        internal ServiceScriptActionIds(
            string backupImagePathId,
            string setStopServiceId,
            string stopServiceId,
            string installServiceId,
            string setInstallStandardId,
            string setInstallUpgradeId)
        {
            BackupImagePathId = backupImagePathId;
            SetStopServiceId = setStopServiceId;
            StopServiceId = stopServiceId;
            InstallServiceId = installServiceId;
            SetInstallStandardId = setInstallStandardId;
            SetInstallUpgradeId = setInstallUpgradeId;
        }

        internal string BackupImagePathId { get; }
        internal string SetStopServiceId { get; }
        internal string StopServiceId { get; }
        internal string InstallServiceId { get; }
        internal string SetInstallStandardId { get; }
        internal string SetInstallUpgradeId { get; }
    }
}
