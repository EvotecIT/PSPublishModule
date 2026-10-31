using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

internal static partial class PowerForgeWixInstallerServiceScriptEmitter
{
    // The command backup remains plain ImagePath for the generated service install script.
    // A separate status sidecar lets rollback restore availability without changing identity.
    private static IEnumerable<string> GetStateActionIds(ServiceScriptActionIds ids)
    {
        yield return ids.BackupImagePathId + "State";
        yield return ids.SetBackupCommandId + "State";
        yield return ids.RollbackImagePathId + "State";
        yield return ids.SetRollbackCommandId + "State";
        yield return ids.RollbackImagePathId + "Stop";
        yield return ids.SetRollbackCommandId + "Stop";
    }

    private static void EmitServiceStateActions(
        PowerForgeInstallerServiceComponent service,
        ServiceScriptActionIds ids,
        string backupPath,
        ICollection<XElement> actions,
        XElement sequence,
        ICollection<UpgradePreparationAction> preparation,
        string condition,
        string stopCondition)
    {
        var backupId = ids.BackupImagePathId + "State";
        var setBackupId = ids.SetBackupCommandId + "State";
        var restoreId = ids.RollbackImagePathId + "State";
        var setRestoreId = ids.SetRollbackCommandId + "State";
        var stopId = ids.RollbackImagePathId + "Stop";
        var setStopId = ids.SetRollbackCommandId + "Stop";
        actions.Add(CreateSetInstallCommand(setBackupId, backupId, BuildStateBackupCommand(service, backupPath)));
        actions.Add(CreateQuietExecAction(backupId, "deferred", hideTarget: true));
        actions.Add(CreateSetInstallCommand(setRestoreId, restoreId, BuildStateRestoreCommand(service, backupPath)));
        actions.Add(CreateQuietExecAction(restoreId, "rollback", hideTarget: true));
        actions.Add(CreateSetInstallCommand(setStopId, stopId, BuildRollbackStopCommand(service)));
        actions.Add(CreateQuietExecAction(stopId, "rollback", hideTarget: true));

        // Rollback reverses execution order: restore files, then ImagePath, then running state.
        var rows = preparation.ToList();
        var index = rows.FindIndex(row => row.ActionId == ids.SetRollbackCommandId);
        if (index < 0) throw new InvalidOperationException("The ImagePath rollback must precede state restoration.");
        rows.InsertRange(index, new[] {
            new UpgradePreparationAction(setBackupId, condition), new UpgradePreparationAction(backupId, condition),
            new UpgradePreparationAction(setRestoreId, condition), new UpgradePreparationAction(restoreId, condition)
        });
        preparation.Clear();
        foreach (var row in rows) preparation.Add(row);

        // This rollback entry is registered after replacement files are installed, so a new
        // service process is stopped before Windows Installer restores the previous files.
        sequence.Add(new XElement(WixNamespace + "Custom", new XAttribute("Action", setStopId),
            new XAttribute("After", "InstallFiles"), new XAttribute("Condition", stopCondition)));
        sequence.Add(new XElement(WixNamespace + "Custom", new XAttribute("Action", stopId),
            new XAttribute("After", setStopId), new XAttribute("Condition", stopCondition)));
    }

    private static string BuildStateBackupCommand(PowerForgeInstallerServiceComponent service, string backupPath)
        => BuildPowerShellCommand(service, "state backup", "$s=(gsv '" + EscapePowerShellSingleQuoted(service.ServiceName) +
            "' -ea Stop).Status;if($s-notin'Stopped','Running'){throw 'Busy'};$s|Out-File '" +
            EscapePowerShellSingleQuoted(backupPath) + ".s' -NoN -NoC -ea Stop");

    private static string BuildStateRestoreCommand(PowerForgeInstallerServiceComponent service, string backupPath)
        => BuildPowerShellCommand(service, "state restore", "$b='" + EscapePowerShellSingleQuoted(backupPath) +
            "';if(Test-Path $b){exit 1};if((gc ($b+'.s') -ea Stop)-eq'Running'){sasv '" +
            EscapePowerShellSingleQuoted(service.ServiceName) + "' -ea Stop};rm ($b+'.s') -ea Stop");

    private static string BuildRollbackStopCommand(PowerForgeInstallerServiceComponent service)
        => BuildPowerShellCommand(service, "rollback stop", "$s=gsv '" + EscapePowerShellSingleQuoted(service.ServiceName) +
            "' -ea 0;if($s-and$s.Status-ne'Stopped'){spsv $s -ea Stop;$s.WaitForStatus('Stopped','00:00:30')}");
}
