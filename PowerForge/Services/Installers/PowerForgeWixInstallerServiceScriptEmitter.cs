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
        string? upgradeSequenceTail = null;
        foreach (var service in definition.Components.OfType<PowerForgeInstallerServiceComponent>())
        {
            if (service.ScriptInstall is null)
                continue;

            upgradeSequenceTail = EmitServiceActions(service, actions, sequence, upgradeSequenceTail);
        }

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
    }

    private static string? EmitServiceActions(
        PowerForgeInstallerServiceComponent service,
        ICollection<XElement> actions,
        XElement sequence,
        string? upgradeSequenceTail)
    {
        var script = service.ScriptInstall!;
        var ids = BuildIds(service.Id);
        var resolvedBackupPath = ResolveBackupPath(service, script);
        var upgradeCondition = CombineConditions(script.Condition, "WIX_UPGRADE_DETECTED", "NOT REMOVE=\"ALL\"");
        var standardCondition = string.IsNullOrWhiteSpace(script.UpgradeCommand)
            ? script.Condition
            : CombineConditions(script.Condition, "NOT WIX_UPGRADE_DETECTED", "NOT REMOVE=\"ALL\"");

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

        actions.Add(CreateSetInstallCommand(ids.SetInstallStandardId, ids.InstallServiceId, script.Command));
        return AddSequenceRows(sequence, ids, script, standardCondition, upgradeCondition, upgradeSequenceTail);
    }

    private static string? AddSequenceRows(
        XElement sequence,
        ServiceScriptActionIds ids,
        PowerForgeInstallerServiceScriptInstall script,
        string standardCondition,
        string upgradeCondition,
        string? upgradeSequenceTail)
    {
        string? tail = upgradeSequenceTail;
        if (script.BackupExistingImagePath)
        {
            AddUpgradeSequenceRow(sequence, ids.SetBackupCommandId, tail, upgradeCondition);
            sequence.Add(new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", ids.BackupImagePathId),
                new XAttribute("After", ids.SetBackupCommandId),
                new XAttribute("Condition", upgradeCondition)));
            tail = ids.BackupImagePathId;
        }

        if (script.StopServiceForUpgrade)
        {
            AddUpgradeSequenceRow(sequence, ids.SetStopServiceId, tail, upgradeCondition);
            sequence.Add(new XElement(
                WixNamespace + "Custom",
                new XAttribute("Action", ids.StopServiceId),
                new XAttribute("After", ids.SetStopServiceId),
                new XAttribute("Condition", upgradeCondition)));
            tail = ids.StopServiceId;
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
        return tail;
    }

    private static void AddUpgradeSequenceRow(
        XElement sequence,
        string actionId,
        string? afterActionId,
        string condition)
    {
        var element = new XElement(
            WixNamespace + "Custom",
            new XAttribute("Action", actionId),
            new XAttribute("Condition", condition));
        if (string.IsNullOrWhiteSpace(afterActionId))
            element.Add(new XAttribute("Before", "RemoveExistingProducts"));
        else
            element.Add(new XAttribute("After", afterActionId!));
        sequence.Add(element);
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

    private static XElement CreateSetQuietExecCommand(string id, string command)
        => CreateSetInstallCommand(id, "WixQuietExecCmdLine", command);

    private static string BuildBackupCommand(
        PowerForgeInstallerServiceComponent service,
        string backupPath)
    {
        var serviceName = EscapeCommandDoubleQuoted(service.ServiceName);
        var backup = EscapeCommandDoubleQuoted(backupPath);
        return "\"[%ComSpec]\" /c (for /f \"tokens=2,*\" %A in ('reg query \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\" +
               serviceName +
               "\" /v ImagePath 2^>nul ^| find \"ImagePath\"') do @echo %B)>\"" +
               backup +
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
            BuildActionId(prefix, "SetInstallServiceUpgrade"));
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
            string setInstallUpgradeId)
        {
            BackupImagePathId = backupImagePathId;
            SetBackupCommandId = setBackupCommandId;
            SetStopServiceId = setStopServiceId;
            StopServiceId = stopServiceId;
            InstallServiceId = installServiceId;
            SetInstallStandardId = setInstallStandardId;
            SetInstallUpgradeId = setInstallUpgradeId;
        }

        internal string BackupImagePathId { get; }
        internal string SetBackupCommandId { get; }
        internal string SetStopServiceId { get; }
        internal string StopServiceId { get; }
        internal string InstallServiceId { get; }
        internal string SetInstallStandardId { get; }
        internal string SetInstallUpgradeId { get; }
    }
}
