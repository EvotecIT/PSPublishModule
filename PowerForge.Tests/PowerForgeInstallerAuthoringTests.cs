using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed class PowerForgeInstallerAuthoringTests
{
    private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";
    private static readonly XNamespace Util = "http://wixtoolset.org/schemas/v4/wxs/util";

    [Fact]
    public void EmitSource_ModelsServiceProgramDataInputsAndPayloadGroup()
    {
        var definition = CreateMonitoringInstaller();

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.Equal("http://wixtoolset.org/schemas/v4/wxs", doc.Root!.Name.NamespaceName);
        Assert.NotNull(doc.Descendants(Wix + "Package").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "TestimoX Monitoring" &&
            (string?)e.Attribute("Scope") == "perMachine"));
        Assert.NotNull(doc.Descendants(Wix + "Property").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "LICENSE_KEY" &&
            (string?)e.Attribute("Secure") == "yes" &&
            (string?)e.Attribute("Hidden") == "yes"));
        Assert.NotNull(doc.Descendants(Wix + "Property").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "MONITORINGDATADIR" &&
            e.Descendants(Wix + "RegistrySearch").Any(search =>
                (string?)search.Attribute("Name") == "DataDir" &&
                (string?)search.Attribute("Type") == "raw")));
        var serviceInstall = doc.Descendants(Wix + "ServiceInstall").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "TestimoX.Monitoring" &&
            (string?)e.Attribute("Arguments") == "--config \"[ProgramDataMonitoring]TestimoX.Monitoring.json\"");
        Assert.NotNull(serviceInstall);
        Assert.Null(serviceInstall!.Attribute("Account"));
        Assert.NotNull(doc.Descendants(Wix + "ServiceControl").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "TestimoX.Monitoring" &&
            (string?)e.Attribute("Start") == "install"));
        Assert.NotNull(doc.Descendants(Wix + "RegistryValue").SingleOrDefault(e =>
            (string?)e.Attribute("Key") == @"SYSTEM\CurrentControlSet\Services\TestimoX.Monitoring" &&
            (string?)e.Attribute("Name") == "DelayedAutoStart"));
        Assert.NotNull(doc.Descendants(Util + "RemoveFolderEx").SingleOrDefault(e =>
            (string?)e.Attribute("Property") == "ProgramDataMonitoring" &&
            (string?)e.Attribute("Condition") == "REMOVE_DATA=1"));
        Assert.Null(doc.Descendants(Wix + "Component")
            .Single(e => (string?)e.Attribute("Id") == "RemoveProgramData")
            .Attribute("Condition"));
        Assert.NotNull(doc.Descendants(Wix + "ComponentGroupRef").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "ProductFiles"));
    }

    [Fact]
    public void EmitSource_ModelsServiceAccountAndPasswordProperties()
    {
        var definition = CreateMonitoringInstaller();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "ServiceAccount",
            PropertyName = "SERVICE_ACCOUNT",
            Label = "Service account",
            DefaultValue = "LocalSystem"
        });
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "ServicePassword",
            PropertyName = "SERVICE_PASSWORD",
            Label = "Service password",
            Kind = PowerForgeInstallerInputKind.Password,
            Secure = true,
            Hidden = true
        });
        definition.LaunchConditions.Add(new PowerForgeInstallerLaunchCondition
        {
            Condition = "SERVICE_ACCOUNT = \"\" OR SERVICE_ACCOUNT = \"LocalSystem\" OR SERVICE_PASSWORD <> \"\"",
            Message = "Password is required when specifying a service account."
        });

        var service = definition.Components.OfType<PowerForgeInstallerServiceComponent>().Single();
        service.AccountPropertyName = "SERVICE_ACCOUNT";
        service.PasswordPropertyName = "SERVICE_PASSWORD";

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "Property").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "SERVICE_ACCOUNT" &&
            (string?)e.Attribute("Value") == "LocalSystem"));
        Assert.NotNull(doc.Descendants(Wix + "Property").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "SERVICE_PASSWORD" &&
            (string?)e.Attribute("Secure") == "yes" &&
            (string?)e.Attribute("Hidden") == "yes"));
        Assert.NotNull(doc.Descendants(Wix + "Launch").SingleOrDefault(e =>
            (string?)e.Attribute("Condition") == "SERVICE_ACCOUNT = \"\" OR SERVICE_ACCOUNT = \"LocalSystem\" OR SERVICE_PASSWORD <> \"\"" &&
            (string?)e.Attribute("Message") == "Password is required when specifying a service account."));
        Assert.NotNull(doc.Descendants(Wix + "ServiceInstall").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "TestimoX.Monitoring" &&
            (string?)e.Attribute("Account") == "[SERVICE_ACCOUNT]" &&
            (string?)e.Attribute("Password") == "[SERVICE_PASSWORD]"));
    }

    [Fact]
    public void EmitSource_RejectsServiceCredentialPropertiesForScriptInstall()
    {
        var definition = CreateMonitoringInstaller();
        var service = definition.Components.OfType<PowerForgeInstallerServiceComponent>().Single();
        service.AccountPropertyName = "SERVICE_ACCOUNT";
        service.ScriptInstall = new PowerForgeInstallerServiceScriptInstall
        {
            Command = "\"powershell.exe\" -NoP -EP Bypass -File \"[INSTALLFOLDER]Install-Service.ps1\""
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("AccountPropertyName", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmitSource_ModelsScriptServiceInstallWithImagePathPreservation()
    {
        var definition = CreateMonitoringInstaller();
        definition.Components.Add(new PowerForgeInstallerFileComponent
        {
            Id = "InstallServiceScriptComponent",
            FileId = "InstallServiceScript",
            Source = "$(var.PayloadDir)\\Install-Service.ps1"
        });
        var service = definition.Components.OfType<PowerForgeInstallerServiceComponent>().Single();
        service.ControlStart = "none";
        service.ControlStop = "uninstall";
        service.ControlRemove = "uninstall";
        service.ScriptInstall = new PowerForgeInstallerServiceScriptInstall
        {
            Command = "\"powershell.exe\" -NoP -EP Bypass -File \"[INSTALLFOLDER]Install-Service.ps1\" -ConfigPath \"[ProgramDataMonitoring]TestimoX.Monitoring.json\" -ServiceName \"TestimoX.Monitoring\"",
            UpgradeCommand = "\"powershell.exe\" -NoP -EP Bypass -File \"[INSTALLFOLDER]Install-Service.ps1\" -ConfigPath \"[ProgramDataMonitoring]TestimoX.Monitoring.json\" -ServiceName \"TestimoX.Monitoring\" -BackupPath \"[TempFolder]tmx-svc.txt\" -PreserveExistingServiceBinPath -UpgradeMode",
            BackupExistingImagePath = true,
            BackupPath = "[TempFolder]tmx-svc.txt",
            StopServiceForUpgrade = true,
            StopDelaySeconds = 30
        };

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);
        var serviceExistsProperty = doc.Descendants(Wix + "Property").SingleOrDefault(e =>
            (string?)e.Attribute("Secure") == "yes" &&
            e.Descendants(Wix + "RegistrySearch").Any(search =>
                (string?)search.Attribute("Root") == "HKLM" &&
                (string?)search.Attribute("Key") == @"SYSTEM\CurrentControlSet\Services\TestimoX.Monitoring" &&
                (string?)search.Attribute("Name") == "ImagePath" &&
                (string?)search.Attribute("Type") == "raw"));
        Assert.NotNull(serviceExistsProperty);
        var serviceExistsPropertyId = (string)serviceExistsProperty!.Attribute("Id")!;

        Assert.Null(doc.Descendants(Wix + "ServiceInstall").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "TestimoX.Monitoring"));
        Assert.NotNull(doc.Descendants(Wix + "ServiceControl").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "TestimoX.Monitoring" &&
            (string?)e.Attribute("Start") == "none" &&
            (string?)e.Attribute("Stop") == "uninstall" &&
            (string?)e.Attribute("Remove") == "uninstall"));
        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "ServiceComponent.SetBackupCommand" &&
            (string?)e.Attribute("Property") == "WixQuietExecCmdLine" &&
            ((string?)e.Attribute("Value"))?.Contains("powershell.exe -NoLogo -NoProfile", StringComparison.Ordinal) == true &&
            ((string?)e.Attribute("Value"))?.Contains("PF_SERVICE=TestimoX.Monitoring", StringComparison.Ordinal) == true &&
            ((string?)e.Attribute("Value"))?.Contains("PF_BACKUP=[TempFolder]tmx-svc.txt", StringComparison.Ordinal) == true &&
            ((string?)e.Attribute("Value"))?.Contains(@"Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services", StringComparison.Ordinal) == true &&
            ((string?)e.Attribute("Value"))?.Contains("ImagePath", StringComparison.Ordinal) == true &&
            ((string?)e.Attribute("Value"))?.Contains("WriteAllText", StringComparison.Ordinal) == true &&
            ((string?)e.Attribute("Value"))?.Contains("WriteAllText($backup", StringComparison.Ordinal) == true));
        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "ServiceComponent.SetStopService" &&
            ((string?)e.Attribute("Value"))?.Contains("exit /b 0", StringComparison.Ordinal) == true));
        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "ServiceComponent.InstallService" &&
            (string?)e.Attribute("DllEntry") == "WixQuietExec" &&
            (string?)e.Attribute("Execute") == "deferred" &&
            (string?)e.Attribute("Impersonate") == "no"));
        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "ServiceComponent.SetInstallServiceUpgrade" &&
            ((string?)e.Attribute("Value"))?.Contains("-PreserveExistingServiceBinPath -UpgradeMode", StringComparison.Ordinal) == true));

        var sequenceRows = doc.Descendants(Wix + "InstallExecuteSequence")
            .Descendants(Wix + "Custom")
            .ToArray();
        var sequenceActions = sequenceRows
            .Select(e => (string?)e.Attribute("Action"))
            .ToArray();
        Assert.Contains("ServiceComponent.BackupImagePath", sequenceActions);
        Assert.Contains("ServiceComponent.SetStopService", sequenceActions);
        Assert.Contains("ServiceComponent.StopService", sequenceActions);
        Assert.Contains("ServiceComponent.SetInstallServiceUpgrade", sequenceActions);
        Assert.Contains("ServiceComponent.InstallService", sequenceActions);
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.SetInstallServiceUpgrade" &&
            ((string?)e.Attribute("Condition"))?.Contains("WIX_UPGRADE_DETECTED OR " + serviceExistsPropertyId, StringComparison.Ordinal) == true));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.SetInstallService" &&
            ((string?)e.Attribute("Condition"))?.Contains("NOT (WIX_UPGRADE_DETECTED OR " + serviceExistsPropertyId + ")", StringComparison.Ordinal) == true));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.SetBackupCommand" &&
            (string?)e.Attribute("Before") == "RemoveExistingProducts"));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.BackupImagePath" &&
            (string?)e.Attribute("After") == "ServiceComponent.SetBackupCommand"));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.SetStopService" &&
            (string?)e.Attribute("After") == "ServiceComponent.BackupImagePath"));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.StopService" &&
            (string?)e.Attribute("After") == "ServiceComponent.SetStopService"));
    }

    [Fact]
    public void EmitSource_ModelsExitDialogLaunchAction()
    {
        var definition = CreateMonitoringInstaller();
        definition.ExitLaunch = new PowerForgeInstallerExitLaunch
        {
            Text = "Open TestimoX Monitoring",
            Target = "http://127.0.0.1:9000/"
        };

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Root!.Attribute(XNamespace.Xmlns + "ui"));
        Assert.NotNull(doc.Root!.Attribute(XNamespace.Xmlns + "util"));
        Assert.NotNull(doc.Descendants(Wix + "Property").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT" &&
            (string?)e.Attribute("Value") == "Open TestimoX Monitoring"));
        Assert.NotNull(doc.Descendants(Wix + "Property").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "WixShellExecTarget" &&
            (string?)e.Attribute("Value") == "http://127.0.0.1:9000/"));
        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "PowerForgeLaunchOnExit" &&
            (string?)e.Attribute("BinaryRef") == "Wix4UtilCA_$(sys.BUILDARCHSHORT)" &&
            (string?)e.Attribute("DllEntry") == "WixShellExec"));
        Assert.NotNull(doc.Descendants(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Dialog") == "ExitDialog" &&
            (string?)e.Attribute("Control") == "Finish" &&
            (string?)e.Attribute("Event") == "DoAction" &&
            (string?)e.Attribute("Value") == "PowerForgeLaunchOnExit"));
    }

    [Fact]
    public void EmitSource_ModelsDeferredExecutableActions()
    {
        var definition = CreateMonitoringInstaller();
        definition.ExecutableActions.Add(new PowerForgeInstallerExecutableAction
        {
            Id = "InitMonitoringConfig",
            FileRef = "MonitoringExe",
            Arguments = "--init-config --force --config \"[ProgramDataMonitoring]TestimoX.Monitoring.json\" --init-preset [INIT_PRESET] --no-console",
            Condition = "INIT_CONFIG=1 AND NOT REMOVE=\"ALL\"",
            After = "InstallFiles"
        });
        definition.ExecutableActions.Add(new PowerForgeInstallerExecutableAction
        {
            Id = "InstallMonitoringLicense",
            FileRef = "MonitoringExe",
            Arguments = "--install-license --license-path \"[LICENSE_PATH]\" --no-console",
            Condition = "LICENSE_PATH<>\"\" AND NOT REMOVE=\"ALL\"",
            Before = "InstallServices",
            After = null
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "InitMonitoringConfig.SetData" &&
            (string?)e.Attribute("Property") == "InitMonitoringConfig" &&
            ((string?)e.Attribute("Value"))?.Contains("--init-preset [INIT_PRESET]", StringComparison.Ordinal) == true));
        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "InitMonitoringConfig" &&
            (string?)e.Attribute("FileRef") == "MonitoringExe" &&
            (string?)e.Attribute("ExeCommand") == "[CustomActionData]" &&
            (string?)e.Attribute("Execute") == "deferred" &&
            (string?)e.Attribute("Impersonate") == "no" &&
            (string?)e.Attribute("HideTarget") == "yes"));
        var sequenceRows = doc.Descendants(Wix + "InstallExecuteSequence")
            .Descendants(Wix + "Custom")
            .ToArray();
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "InitMonitoringConfig.SetData" &&
            (string?)e.Attribute("After") == "InstallFiles"));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "InitMonitoringConfig" &&
            (string?)e.Attribute("After") == "InitMonitoringConfig.SetData"));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "InstallMonitoringLicense.SetData" &&
            (string?)e.Attribute("Before") == "InstallMonitoringLicense"));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "InstallMonitoringLicense" &&
            (string?)e.Attribute("Before") == "InstallServices"));
    }

    [Fact]
    public void EmitSource_ScriptInstallCanOwnServiceUninstallWithoutServiceControl()
    {
        var definition = CreateMonitoringInstaller();
        var service = definition.Components.OfType<PowerForgeInstallerServiceComponent>().Single();
        service.ScriptInstall = new PowerForgeInstallerServiceScriptInstall
        {
            Command = "\"[INSTALLFOLDER]Monitoring.exe\" --install --name \"TestimoX.Monitoring\"",
            UpgradeCommand = "\"[INSTALLFOLDER]Monitoring.exe\" --install --name \"TestimoX.Monitoring\" --preserve-existing-service-binpath",
            UninstallCommand = "\"[INSTALLFOLDER]Monitoring.exe\" --uninstall --name \"TestimoX.Monitoring\"",
            SuppressServiceControl = true,
            BackupExistingImagePath = true,
            StopServiceForUpgrade = true
        };

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.Null(doc.Descendants(Wix + "ServiceInstall").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "TestimoX.Monitoring"));
        Assert.Null(doc.Descendants(Wix + "ServiceControl").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "TestimoX.Monitoring"));
        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "ServiceComponent.UninstallService" &&
            (string?)e.Attribute("DllEntry") == "WixQuietExec" &&
            (string?)e.Attribute("Execute") == "deferred" &&
            (string?)e.Attribute("Impersonate") == "no"));
        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "ServiceComponent.SetUninstallService" &&
            ((string?)e.Attribute("Value"))?.Contains("--uninstall --name", StringComparison.Ordinal) == true));
        var serviceExistsProperty = doc.Descendants(Wix + "Property").SingleOrDefault(e =>
            (string?)e.Attribute("Secure") == "yes" &&
            e.Descendants(Wix + "RegistrySearch").Any(search =>
                (string?)search.Attribute("Root") == "HKLM" &&
                (string?)search.Attribute("Key") == @"SYSTEM\CurrentControlSet\Services\TestimoX.Monitoring" &&
                (string?)search.Attribute("Name") == "ImagePath" &&
                (string?)search.Attribute("Type") == "raw"));
        Assert.NotNull(serviceExistsProperty);
        var serviceExistsPropertyId = (string)serviceExistsProperty!.Attribute("Id")!;

        var sequenceRows = doc.Descendants(Wix + "InstallExecuteSequence")
            .Descendants(Wix + "Custom")
            .ToArray();
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.SetUninstallService" &&
            (string?)e.Attribute("Before") == "ServiceComponent.UninstallService" &&
            ((string?)e.Attribute("Condition"))?.Contains("REMOVE=\"ALL\" AND NOT UPGRADINGPRODUCTCODE", StringComparison.Ordinal) == true &&
            ((string?)e.Attribute("Condition"))?.Contains(serviceExistsPropertyId, StringComparison.Ordinal) == true));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.UninstallService" &&
            (string?)e.Attribute("Before") == "RemoveFiles" &&
            ((string?)e.Attribute("Condition"))?.Contains("REMOVE=\"ALL\" AND NOT UPGRADINGPRODUCTCODE", StringComparison.Ordinal) == true &&
            ((string?)e.Attribute("Condition"))?.Contains(serviceExistsPropertyId, StringComparison.Ordinal) == true));
    }

    [Fact]
    public void EmitSource_SuppressServiceControlRequiresScriptUninstallCommand()
    {
        var definition = CreateMonitoringInstaller();
        var service = definition.Components.OfType<PowerForgeInstallerServiceComponent>().Single();
        service.ScriptInstall = new PowerForgeInstallerServiceScriptInstall
        {
            Command = "\"[INSTALLFOLDER]Monitoring.exe\" --install --name \"TestimoX.Monitoring\"",
            SuppressServiceControl = true
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));

        Assert.Contains("SuppressServiceControl requires ScriptInstall.UninstallCommand", exception.Message);
    }

    [Fact]
    public void EmitSource_ScriptUninstallCommandRequiresSuppressingServiceControl()
    {
        var definition = CreateMonitoringInstaller();
        var service = definition.Components.OfType<PowerForgeInstallerServiceComponent>().Single();
        service.ScriptInstall = new PowerForgeInstallerServiceScriptInstall
        {
            Command = "\"[INSTALLFOLDER]Monitoring.exe\" --install --name \"TestimoX.Monitoring\"",
            UninstallCommand = "\"[INSTALLFOLDER]Monitoring.exe\" --uninstall --name \"TestimoX.Monitoring\""
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));

        Assert.Contains("UninstallCommand requires ScriptInstall.SuppressServiceControl", exception.Message);
    }

    [Fact]
    public void EmitSource_ModelsLicenseAgreement()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.LicenseAgreement = new PowerForgeInstallerLicenseAgreement
        {
            Path = "License.rtf"
        };

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Root!.Attribute(XNamespace.Xmlns + "ui"));
        Assert.NotNull(doc.Descendants(Wix + "WixVariable").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "WixUILicenseRtf" &&
            (string?)e.Attribute("Value") == "License.rtf"));
        Assert.NotNull(doc.Descendants(Wix + "UI").SingleOrDefault());
    }

    [Fact]
    public void EmitSource_ModelsComboBoxAndDialogActionButton()
    {
        var definition = CreateMonitoringInstaller();
        var preset = definition.Inputs.Single(input => input.Id == "Preset");
        preset.Kind = PowerForgeInstallerInputKind.ComboBox;
        var dialog = definition.Dialogs.Single(dialog => dialog.Id == "ConfigurationDlg");
        dialog.Actions.Add(new PowerForgeInstallerDialogAction
        {
            Id = "OpenStudio",
            Text = "Open Studio",
            Target = "http://127.0.0.1:58433/studio"
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Root!.Attribute(XNamespace.Xmlns + "util"));
        Assert.NotNull(doc.Descendants(Wix + "CustomAction").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "PowerForgeDialogShellExecute" &&
            (string?)e.Attribute("DllEntry") == "WixShellExec"));
        Assert.NotNull(doc.Descendants(Wix + "Control").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "Preset" &&
            (string?)e.Attribute("Type") == "ComboBox" &&
            e.Descendants(Wix + "ListItem").Any(item =>
                (string?)item.Attribute("Value") == "core" &&
                (string?)item.Attribute("Text") == "Core AD")));

        var action = doc.Descendants(Wix + "Control").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "OpenStudio" &&
            (string?)e.Attribute("Type") == "PushButton" &&
            (string?)e.Attribute("Text") == "Open Studio");
        Assert.NotNull(action);
        Assert.NotNull(action!.Elements(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Property") == "WixShellExecTarget" &&
            (string?)e.Attribute("Value") == "http://127.0.0.1:58433/studio" &&
            (string?)e.Attribute("Order") == "1"));
        Assert.NotNull(action.Elements(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Event") == "DoAction" &&
            (string?)e.Attribute("Value") == "PowerForgeDialogShellExecute" &&
            (string?)e.Attribute("Order") == "2"));
    }

    [Fact]
    public void EmitSource_RestoresExitLaunchTargetAfterDialogAction()
    {
        var definition = CreateMonitoringInstaller();
        definition.ExitLaunch = new PowerForgeInstallerExitLaunch
        {
            Text = "Open monitoring",
            Target = "http://127.0.0.1:9000/"
        };
        var dialog = definition.Dialogs.Single(dialog => dialog.Id == "ConfigurationDlg");
        dialog.Actions.Add(new PowerForgeInstallerDialogAction
        {
            Id = "OpenStudio",
            Text = "Open Studio",
            Target = "http://127.0.0.1:58433/studio"
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);
        var action = doc.Descendants(Wix + "Control").Single(e =>
            (string?)e.Attribute("Id") == "OpenStudio");

        Assert.NotNull(action.Elements(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Property") == "WixShellExecTarget" &&
            (string?)e.Attribute("Value") == "http://127.0.0.1:58433/studio" &&
            (string?)e.Attribute("Order") == "1"));
        Assert.NotNull(action.Elements(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Event") == "DoAction" &&
            (string?)e.Attribute("Value") == "PowerForgeDialogShellExecute" &&
            (string?)e.Attribute("Order") == "2"));
        Assert.NotNull(action.Elements(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Property") == "WixShellExecTarget" &&
            (string?)e.Attribute("Value") == "http://127.0.0.1:9000/" &&
            (string?)e.Attribute("Order") == "3"));
    }

    [Fact]
    public void EmitSource_UsesUniqueScriptServiceActionIdsForLongComponentNames()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Clear();
        AddScriptService(definition, "ServiceComponentWithVeryLongSharedPrefixForTenantAlpha");
        AddScriptService(definition, "ServiceComponentWithVeryLongSharedPrefixForTenantBeta");

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        string[] actionIds = doc.Descendants(Wix + "CustomAction")
            .Select(e => (string?)e.Attribute("Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
        Assert.Equal(actionIds.Length, actionIds.Distinct(StringComparer.Ordinal).Count());

        string[] quietExecSetterIds = doc.Descendants(Wix + "CustomAction")
            .Where(e => (string?)e.Attribute("Property") == "WixQuietExecCmdLine")
            .Select(e => (string?)e.Attribute("Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
        Assert.Equal(4, quietExecSetterIds.Length);
        Assert.Equal(quietExecSetterIds.Length, quietExecSetterIds.Distinct(StringComparer.Ordinal).Count());

        string[] sequenceActions = doc.Descendants(Wix + "InstallExecuteSequence")
            .Descendants(Wix + "Custom")
            .Select(e => (string?)e.Attribute("Action"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
        int alphaBackup = Array.FindIndex(sequenceActions, id => id.EndsWith("BackupImagePath", StringComparison.Ordinal));
        int betaBackupSetter = Array.FindLastIndex(sequenceActions, id => id.Contains("SetBackupCommand", StringComparison.Ordinal));
        Assert.True(alphaBackup >= 0);
        Assert.True(betaBackupSetter > alphaBackup);
    }

    [Fact]
    public void EmitSource_UsesUniqueScriptServiceActionIdsForOverlappingComponentNames()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Clear();
        AddScriptService(definition, "ASet");
        AddScriptService(definition, "A");

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        string[] actionIds = doc.Descendants(Wix + "CustomAction")
            .Select(e => (string?)e.Attribute("Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
        Assert.Equal(actionIds.Length, actionIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("ASet.InstallService", actionIds);
        Assert.Contains("A.SetInstallService", actionIds);
    }

    [Fact]
    public void EmitSource_UsesUniqueDefaultScriptServiceBackupPaths()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Clear();
        AddScriptService(definition, "ServiceOne");
        AddScriptService(definition, "ServiceTwo");

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        string[] backupCommands = doc.Descendants(Wix + "CustomAction")
            .Where(e => (string?)e.Attribute("Property") == "WixQuietExecCmdLine")
            .Select(e => (string?)e.Attribute("Value"))
            .Where(value => value?.Contains("ImagePath", StringComparison.Ordinal) == true)
            .Select(value => value!)
            .ToArray();
        Assert.Contains(backupCommands, command =>
            command.Contains("[TempFolder]powerforge-ServiceOne-service-binpath.txt", StringComparison.Ordinal));
        Assert.Contains(backupCommands, command =>
            command.Contains("[TempFolder]powerforge-ServiceTwo-service-binpath.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void EmitSource_ScriptServiceBackupUsesEnvironmentForRuntimeExpandedPath()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Clear();
        definition.Components.Add(new PowerForgeInstallerServiceComponent
        {
            Id = "ServiceComponent",
            FileId = "ServiceExe",
            Source = "$(var.PayloadDir)\\Service.exe",
            ServiceName = "Contoso.Service",
            DisplayName = "Contoso Service",
            ScriptInstall = new PowerForgeInstallerServiceScriptInstall
            {
                Command = "\"powershell.exe\" -NoP -EP Bypass -File \"[INSTALLFOLDER]Install-Service.ps1\"",
                BackupExistingImagePath = true,
                BackupPath = "[TempFolder]O'Connor\\service.txt"
            }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        string command = doc.Descendants(Wix + "CustomAction")
            .Where(e => (string?)e.Attribute("Id") == "ServiceComponent.SetBackupCommand")
            .Select(e => (string?)e.Attribute("Value"))
            .Single(value => !string.IsNullOrWhiteSpace(value))!;
        Assert.Contains("set \"PF_BACKUP=[TempFolder]O'Connor\\service.txt\"", command, StringComparison.Ordinal);
        Assert.Contains("[System.IO.File]::WriteAllText($backup", command, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteAllText('[TempFolder]O''Connor", command, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_DoesNotUseExistingServiceUpgradeSignalWhenServiceControlRemovesOnInstall()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Clear();
        definition.Components.Add(new PowerForgeInstallerServiceComponent
        {
            Id = "ServiceComponent",
            FileId = "ServiceExe",
            Source = "$(var.PayloadDir)\\Service.exe",
            ServiceName = "Contoso.Service",
            DisplayName = "Contoso Service",
            ControlRemove = "both",
            ScriptInstall = new PowerForgeInstallerServiceScriptInstall
            {
                Command = "\"[INSTALLFOLDER]Service.exe\" --install",
                UpgradeCommand = "\"[INSTALLFOLDER]Service.exe\" --install --preserve-existing-service-binpath"
            }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        var sequenceRows = doc.Descendants(Wix + "InstallExecuteSequence")
            .Descendants(Wix + "Custom")
            .ToArray();
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.SetInstallServiceUpgrade" &&
            (string?)e.Attribute("Condition") == "(NOT REMOVE=\"ALL\") AND (WIX_UPGRADE_DETECTED)"));
        Assert.NotNull(sequenceRows.SingleOrDefault(e =>
            (string?)e.Attribute("Action") == "ServiceComponent.SetInstallService" &&
            (string?)e.Attribute("Condition") == "(NOT REMOVE=\"ALL\") AND (NOT (WIX_UPGRADE_DETECTED))"));
    }

    [Fact]
    public void EmitSource_GatesUpgradePrepActionsWithScriptInstallCondition()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Clear();
        definition.Components.Add(new PowerForgeInstallerServiceComponent
        {
            Id = "ConditionalService",
            FileId = "ConditionalServiceExe",
            Source = "$(var.PayloadDir)\\ConditionalService.exe",
            ServiceName = "Conditional.Service",
            DisplayName = "Conditional Service",
            ScriptInstall = new PowerForgeInstallerServiceScriptInstall
            {
                Command = "\"powershell.exe\" -NoP -EP Bypass -File \"[INSTALLFOLDER]Install-Service.ps1\"",
                Condition = "INSTALL_CONDITIONAL_SERVICE=1 AND NOT REMOVE=\"ALL\"",
                BackupExistingImagePath = true,
                StopServiceForUpgrade = true
            }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        var upgradePrepRows = doc.Descendants(Wix + "InstallExecuteSequence")
            .Descendants(Wix + "Custom")
            .Where(e =>
                string.Equals((string?)e.Attribute("Action"), "ConditionalService.SetBackupCommand", StringComparison.Ordinal) ||
                string.Equals((string?)e.Attribute("Action"), "ConditionalService.BackupImagePath", StringComparison.Ordinal) ||
                string.Equals((string?)e.Attribute("Action"), "ConditionalService.SetStopService", StringComparison.Ordinal) ||
                string.Equals((string?)e.Attribute("Action"), "ConditionalService.StopService", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(4, upgradePrepRows.Length);
        Assert.All(upgradePrepRows, row =>
        {
            var condition = (string?)row.Attribute("Condition");
            Assert.Contains("INSTALL_CONDITIONAL_SERVICE=1", condition, StringComparison.Ordinal);
            Assert.Contains("WIX_UPGRADE_DETECTED", condition, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EmitSource_ModelsReusableInputDialogControls()
    {
        var definition = CreateMonitoringInstaller();

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        var dialog = doc.Descendants(Wix + "Dialog").Single(e => (string?)e.Attribute("Id") == "ConfigurationDlg");
        Assert.NotNull(dialog.Descendants(Wix + "Control").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "LicenseKey" &&
            (string?)e.Attribute("Type") == "Edit" &&
            (string?)e.Attribute("Password") == "yes"));
        Assert.NotNull(dialog.Descendants(Wix + "Control").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "RemoveData" &&
            (string?)e.Attribute("Type") == "CheckBox" &&
            (string?)e.Attribute("Property") == "REMOVE_DATA"));
        var next = dialog.Descendants(Wix + "Control").Single(e =>
            (string?)e.Attribute("Id") == "Next");
        var newDialogPublish = next.Descendants(Wix + "Publish")
            .SingleOrDefault(e =>
                (string?)e.Attribute("Event") == "NewDialog" &&
                (string?)e.Attribute("Condition") == "LICENSE_KEY <> \"\"" &&
                (string?)e.Attribute("Value") == "VerifyReadyDlg");
        var spawnDialogPublish = next.Descendants(Wix + "Publish")
            .SingleOrDefault(e =>
                (string?)e.Attribute("Event") == "SpawnDialog" &&
                (string?)e.Attribute("Value") == "PowerForgeRequiredInputDlg" &&
                (string?)e.Attribute("Condition") == "LICENSE_KEY = \"\"");
        Assert.NotNull(newDialogPublish);
        Assert.Equal("2", (string?)newDialogPublish.Attribute("Order"));
        Assert.NotNull(spawnDialogPublish);
        Assert.Equal("1", (string?)spawnDialogPublish.Attribute("Order"));
        var requiredDialog = doc.Descendants(Wix + "Dialog").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "PowerForgeRequiredInputDlg");
        Assert.NotNull(requiredDialog);
        Assert.NotNull(requiredDialog.Descendants(Wix + "Control").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "Message" &&
            (string?)e.Attribute("Text") == "Fill the required field before continuing: License key."));
        Assert.Equal(2, dialog.Descendants(Wix + "RadioButton").Count());
    }

    [Fact]
    public void EmitSource_DoesNotInitializeUncheckedCheckboxProperties()
    {
        var definition = CreateMonitoringInstaller();

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.Null(doc.Descendants(Wix + "Property")
            .SingleOrDefault(e => (string?)e.Attribute("Id") == "REMOVE_DATA"));
    }

    [Fact]
    public void EmitSource_InitializesCheckedCheckboxProperties()
    {
        var definition = CreateMonitoringInstaller();
        definition.Inputs.Single(input => input.Id == "RemoveData").DefaultValue = "true";

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "Property")
            .SingleOrDefault(e =>
                (string?)e.Attribute("Id") == "REMOVE_DATA" &&
                (string?)e.Attribute("Value") == "1"));
    }

    [Fact]
    public void EmitSource_DoesNotAddPublishOrderWhenDialogHasNoRequiredInputs()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "OptionalSetting",
            PropertyName = "OPTIONAL_SETTING",
            Label = "Optional setting"
        });
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "OptionalDlg",
            Title = "Optional settings",
            InputIds = { "OptionalSetting" }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.DoesNotContain(doc.Descendants(Wix + "Dialog"), e =>
            (string?)e.Attribute("Id") == "PowerForgeRequiredInputDlg");

        var nextPublish = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "OptionalDlg")
            .Descendants(Wix + "Control")
            .Single(e => (string?)e.Attribute("Id") == "Next")
            .Descendants(Wix + "Publish")
            .Single(e => (string?)e.Attribute("Event") == "NewDialog");

        Assert.Equal("1", (string?)nextPublish.Attribute("Condition"));
        Assert.Null(nextPublish.Attribute("Order"));
    }

    [Fact]
    public void EmitSource_UsesRequiredMessageForSingleRequiredInputPrompt()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            Required = true,
            RequiredMessage = "Enter a license key before continuing."
        });
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "LicenseDlg",
            Title = "License",
            InputIds = { "LicenseKey" }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        var dialog = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "PowerForgeRequiredInputDlg");
        var messageControl = dialog
            .Descendants(Wix + "Control")
            .SingleOrDefault(e => (string?)e.Attribute("Id") == "Message");

        Assert.NotNull(messageControl);
        Assert.Equal("Enter a license key before continuing.", (string?)messageControl.Attribute("Text"));
    }

    [Fact]
    public void EmitSource_FallsBackToInputLabelForSingleRequiredInputPrompt()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            Required = true
        });
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "LicenseDlg",
            Title = "License",
            InputIds = { "LicenseKey" }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        var dialog = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "PowerForgeRequiredInputDlg");
        var messageControl = dialog
            .Descendants(Wix + "Control")
            .SingleOrDefault(e => (string?)e.Attribute("Id") == "Message");

        Assert.NotNull(messageControl);
        Assert.Equal("Fill the required field before continuing: License key.", (string?)messageControl.Attribute("Text"));
    }

    [Fact]
    public void EmitSource_RequiresCheckboxInputsWhenConfigured()
    {
        var definition = CreateMonitoringInstaller();
        definition.Inputs.Single(input => input.Id == "RemoveData").Required = true;

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        var next = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "ConfigurationDlg")
            .Descendants(Wix + "Control")
            .Single(e => (string?)e.Attribute("Id") == "Next");

        Assert.NotNull(next.Descendants(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Event") == "SpawnDialog" &&
            (string?)e.Attribute("Value") == "PowerForgeRequiredInputDlg" &&
            (string?)e.Attribute("Condition") == "LICENSE_KEY = \"\" OR REMOVE_DATA = \"\""));
        Assert.NotNull(next.Descendants(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Event") == "NewDialog" &&
            (string?)e.Attribute("Condition") == "LICENSE_KEY <> \"\" AND REMOVE_DATA <> \"\""));
    }

    [Fact]
    public void EmitSource_RejectsReservedRequiredInputDialogId()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "PowerForgeRequiredInputDlg",
            Title = "Reserved"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmitSource_RejectsReservedRequiredInputDialogIdPrefix()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "PowerForgeRequiredInputDlg2",
            Title = "Reserved"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmitSource_RejectsRequiredRadioGroupWithoutDefaultValue()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        var input = new PowerForgeInstallerInput
        {
            Id = "Preset",
            PropertyName = "INIT_PRESET",
            Label = "Configuration preset",
            Kind = PowerForgeInstallerInputKind.RadioGroup,
            Required = true
        };
        input.Choices.Add(new PowerForgeInstallerInputChoice { Value = "none", Text = "None" });
        input.Choices.Add(new PowerForgeInstallerInputChoice { Value = "core", Text = "Core AD" });
        definition.Inputs.Add(input);
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ConfigurationDlg",
            Title = "Configuration",
            InputIds = { "Preset" }
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("required radio group", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmitSource_RejectsRequiredRadioGroupWithUnknownDefaultValue()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        var input = new PowerForgeInstallerInput
        {
            Id = "Preset",
            PropertyName = "INIT_PRESET",
            Label = "Configuration preset",
            Kind = PowerForgeInstallerInputKind.RadioGroup,
            Required = true,
            DefaultValue = "missing"
        };
        input.Choices.Add(new PowerForgeInstallerInputChoice { Value = "none", Text = "None" });
        input.Choices.Add(new PowerForgeInstallerInputChoice { Value = "core", Text = "Core AD" });
        definition.Inputs.Add(input);
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ConfigurationDlg",
            Title = "Configuration",
            InputIds = { "Preset" }
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("default value", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmitSource_WiresCustomDialogsIntoInstallDirFlow()
    {
        var definition = CreateMonitoringInstaller();
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "AdvancedDlg",
            Title = "Advanced",
            Description = "Choose advanced options.",
            InputIds = { "RemoveData" }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Dialog") == "InstallDirDlg" &&
            (string?)e.Attribute("Control") == "Next" &&
            (string?)e.Attribute("Event") == "NewDialog" &&
            (string?)e.Attribute("Value") == "ConfigurationDlg"));
        Assert.NotNull(doc.Descendants(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Dialog") == "VerifyReadyDlg" &&
            (string?)e.Attribute("Control") == "Back" &&
            (string?)e.Attribute("Event") == "NewDialog" &&
            (string?)e.Attribute("Value") == "AdvancedDlg"));

        var firstDialogNext = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "ConfigurationDlg")
            .Descendants(Wix + "Control")
            .Single(e => (string?)e.Attribute("Id") == "Next")
            .Descendants(Wix + "Publish")
            .Single(e => (string?)e.Attribute("Event") == "NewDialog");
        Assert.Equal("AdvancedDlg", (string?)firstDialogNext.Attribute("Value"));
        Assert.Equal("LICENSE_KEY <> \"\"", (string?)firstDialogNext.Attribute("Condition"));
        Assert.Equal("2", (string?)firstDialogNext.Attribute("Order"));

        var secondDialogBack = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "AdvancedDlg")
            .Descendants(Wix + "Control")
            .Single(e => (string?)e.Attribute("Id") == "Back")
            .Descendants(Wix + "Publish")
            .Single(e => (string?)e.Attribute("Event") == "NewDialog");
        Assert.Equal("ConfigurationDlg", (string?)secondDialogBack.Attribute("Value"));

        var secondDialogNext = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "AdvancedDlg")
            .Descendants(Wix + "Control")
            .Single(e => (string?)e.Attribute("Id") == "Next")
            .Descendants(Wix + "Publish")
            .Single(e => (string?)e.Attribute("Event") == "NewDialog");
        Assert.Equal("VerifyReadyDlg", (string?)secondDialogNext.Attribute("Value"));
        Assert.Equal("1", (string?)secondDialogNext.Attribute("Condition"));
        Assert.Null(secondDialogNext.Attribute("Order"));
        Assert.Single(doc.Descendants(Wix + "Dialog"), e =>
            (string?)e.Attribute("Id") == "PowerForgeRequiredInputDlg");
    }

    [Fact]
    public void EmitSource_UsesDialogSpecificRequiredInputPrompts()
    {
        var definition = CreateMonitoringInstaller();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "ApiEndpoint",
            PropertyName = "API_ENDPOINT",
            Label = "API endpoint",
            Required = true
        });
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "ApiToken",
            PropertyName = "API_TOKEN",
            Label = "API token",
            Kind = PowerForgeInstallerInputKind.Password,
            Required = true,
            Secure = true,
            Hidden = true
        });
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "AdvancedDlg",
            Title = "Advanced",
            Description = "Choose advanced options.",
            InputIds = { "ApiEndpoint", "ApiToken" }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        var firstNext = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "ConfigurationDlg")
            .Descendants(Wix + "Control")
            .Single(e => (string?)e.Attribute("Id") == "Next");
        Assert.NotNull(firstNext.Descendants(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Event") == "SpawnDialog" &&
            (string?)e.Attribute("Value") == "PowerForgeRequiredInputDlg" &&
            (string?)e.Attribute("Condition") == "LICENSE_KEY = \"\""));

        var secondNext = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "AdvancedDlg")
            .Descendants(Wix + "Control")
            .Single(e => (string?)e.Attribute("Id") == "Next");
        Assert.NotNull(secondNext.Descendants(Wix + "Publish").SingleOrDefault(e =>
            (string?)e.Attribute("Event") == "SpawnDialog" &&
            (string?)e.Attribute("Value") == "PowerForgeRequiredInputDlg2" &&
            (string?)e.Attribute("Condition") == "API_ENDPOINT = \"\" OR API_TOKEN = \"\""));

        Assert.NotNull(doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "PowerForgeRequiredInputDlg")
            .Descendants(Wix + "Control")
            .SingleOrDefault(e =>
                (string?)e.Attribute("Id") == "Message" &&
                (string?)e.Attribute("Text") == "Fill the required field before continuing: License key."));
        Assert.NotNull(doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "PowerForgeRequiredInputDlg2")
            .Descendants(Wix + "Control")
            .SingleOrDefault(e =>
                (string?)e.Attribute("Id") == "Message" &&
                (string?)e.Attribute("Text") == "Fill required fields before continuing: API endpoint; API token."));
    }

    [Fact]
    public void EmitSource_CapsLongRequiredInputPromptLists()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        for (var i = 1; i <= 5; i++)
        {
            definition.Inputs.Add(new PowerForgeInstallerInput
            {
                Id = "Setting" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PropertyName = "SETTING_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Label = "Setting " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Required = true
            });
        }

        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "SettingsDlg",
            Title = "Settings",
            InputIds = { "Setting1", "Setting2", "Setting3", "Setting4", "Setting5" }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "PowerForgeRequiredInputDlg")
            .Descendants(Wix + "Control")
            .SingleOrDefault(e =>
                (string?)e.Attribute("Id") == "Message" &&
                (string?)e.Attribute("Text") == "Fill required fields before continuing: Setting 1; Setting 2; Setting 3; Setting 4; and 1 more."));
    }

    [Fact]
    public void EmitSource_ModelsShortcutsWithRegistryKeyPath()
    {
        var definition = new PowerForgeInstallerDefinition
        {
            Product =
            {
                Name = "IntelligenceX Chat",
                Manufacturer = "Evotec",
                Version = "1.0.0",
                UpgradeCode = "{a2b787a5-f539-4763-add6-2baa2c2518c7}"
            },
            CompanyFolderName = "Evotec",
            InstallDirectoryName = "IntelligenceX Chat",
            PayloadComponentGroupId = "ProductFiles"
        };
        definition.Directories.Add(new PowerForgeInstallerDirectoryTree
        {
            StandardDirectoryId = "ProgramMenuFolder",
            Segments =
            {
                new PowerForgeInstallerDirectorySegment { Id = "ApplicationProgramsFolder", Name = "IntelligenceX" }
            }
        });
        definition.Components.Add(new PowerForgeInstallerShortcutComponent
        {
            Id = "StartMenuShortcutComponent",
            DirectoryRefId = "ApplicationProgramsFolder",
            ShortcutId = "StartMenuShortcut",
            Name = "IntelligenceX Chat",
            TargetFileId = "PrimaryExeFile",
            Arguments = "--open",
            RegistryKey = @"Software\Evotec\IntelligenceX\Chat"
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "StandardDirectory").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "ProgramMenuFolder"));
        Assert.NotNull(doc.Descendants(Wix + "Shortcut").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "IntelligenceX Chat" &&
            (string?)e.Attribute("Target") == "[#PrimaryExeFile]" &&
            (string?)e.Attribute("Arguments") == "--open"));
        Assert.NotNull(doc.Descendants(Wix + "RegistryValue").SingleOrDefault(e =>
            (string?)e.Attribute("KeyPath") == "yes" &&
            (string?)e.Attribute("Root") == "HKCU" &&
            (string?)e.Attribute("Key") == @"Software\Evotec\IntelligenceX\Chat"));
        Assert.NotNull(doc.Descendants(Wix + "RemoveFolder").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "StartMenuShortcutComponentRemoveFolder" &&
            (string?)e.Attribute("On") == "uninstall"));
    }

    [Fact]
    public void EmitSource_UsesPerUserInstallRootForPerUserPackages()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Product.Scope = PowerForgeInstallerScope.PerUser;

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "StandardDirectory").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "LocalAppDataFolder"));
        Assert.DoesNotContain(doc.Descendants(Wix + "StandardDirectory"), e =>
            (string?)e.Attribute("Id") == "ProgramFiles64Folder");
    }

    [Fact]
    public void EmitSource_EscapesRequiredPromptFormattedCharacters()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "ApiToken",
            PropertyName = "API_TOKEN",
            Label = "API [TOKEN]",
            Required = true
        });
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ApiDlg",
            Title = "API",
            InputIds = { "ApiToken" }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        var message = doc.Descendants(Wix + "Dialog")
            .Single(e => (string?)e.Attribute("Id") == "PowerForgeRequiredInputDlg")
            .Descendants(Wix + "Control")
            .Single(e => (string?)e.Attribute("Id") == "Message");
        Assert.Equal("Fill the required field before continuing: API [\\[]TOKEN[\\]].", (string?)message.Attribute("Text"));
    }

    [Fact]
    public void EmitSource_ModelsShortcutsWithDirectTarget()
    {
        var definition = new PowerForgeInstallerDefinition
        {
            Product =
            {
                Name = "DomainDetective",
                Manufacturer = "Evotec",
                Version = "1.0.0",
                UpgradeCode = "{7a89ea55-159e-43e3-98dc-3d220c5c7f6b}"
            },
            CompanyFolderName = "Evotec",
            InstallDirectoryName = "DomainDetective",
            PayloadComponentGroupId = "ProductFiles"
        };
        definition.Directories.Add(new PowerForgeInstallerDirectoryTree
        {
            StandardDirectoryId = "ProgramMenuFolder",
            Segments =
            {
                new PowerForgeInstallerDirectorySegment { Id = "ApplicationProgramsFolder", Name = "DomainDetective" }
            }
        });
        definition.Components.Add(new PowerForgeInstallerShortcutComponent
        {
            Id = "StartMenuShortcutComponent",
            DirectoryRefId = "ApplicationProgramsFolder",
            ShortcutId = "StartMenuShortcut",
            Name = "DomainDetective",
            Target = "[INSTALLFOLDER]DomainDetective.exe",
            RegistryKey = @"Software\Evotec\DomainDetective"
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "Shortcut").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "DomainDetective" &&
            (string?)e.Attribute("Target") == "[INSTALLFOLDER]DomainDetective.exe"));
    }

    [Fact]
    public void EmitSource_ModelsDirectoryTreesUnderExistingDirectoryRefs()
    {
        var definition = new PowerForgeInstallerDefinition
        {
            Product =
            {
                Name = "TierBridge",
                Manufacturer = "Evotec",
                Version = "1.0.0",
                UpgradeCode = "{2057CC80-6B76-4565-9B3A-1459AA760B42}"
            },
            CompanyFolderName = "Evotec",
            UseCompanyFolder = false,
            InstallDirectoryName = "TierBridge",
            PayloadComponentGroupId = "TierBridgePayload"
        };
        definition.Directories.Add(new PowerForgeInstallerDirectoryTree
        {
            DirectoryRefId = "INSTALLFOLDER",
            Segments =
            {
                new PowerForgeInstallerDirectorySegment { Id = "SERVICEFOLDER", Name = "Service" }
            }
        });
        definition.Components.Add(new PowerForgeInstallerServiceComponent
        {
            Id = "TierBridgeService",
            DirectoryRefId = "SERVICEFOLDER",
            FileId = "TierBridgeServiceExe",
            Source = "$(var.PayloadDir)\\Service\\TierBridge.Service.exe",
            ServiceName = "TierBridge",
            DisplayName = "TierBridge Transfer Service"
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        var programFilesRoot = doc.Descendants(Wix + "StandardDirectory")
            .Single(e => (string?)e.Attribute("Id") == "ProgramFiles64Folder");
        Assert.NotNull(programFilesRoot.Elements(Wix + "Directory").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "INSTALLFOLDER" &&
            (string?)e.Attribute("Name") == "TierBridge"));
        Assert.DoesNotContain(programFilesRoot.Descendants(Wix + "Directory"), e =>
            (string?)e.Attribute("Id") == "CompanyFolder");

        var serviceDirectory = doc.Descendants(Wix + "DirectoryRef")
            .Single(e => (string?)e.Attribute("Id") == "INSTALLFOLDER")
            .Descendants(Wix + "Directory")
            .Single(e => (string?)e.Attribute("Id") == "SERVICEFOLDER");
        Assert.Equal("Service", (string?)serviceDirectory.Attribute("Name"));
        Assert.NotNull(doc.Descendants(Wix + "DirectoryRef").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "SERVICEFOLDER"));
        Assert.NotNull(doc.Descendants(Wix + "File").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "TierBridgeServiceExe" &&
            (string?)e.Attribute("Source") == "$(var.PayloadDir)\\Service\\TierBridge.Service.exe"));
    }

    [Fact]
    public void EmitSource_ModelsRegistryValueFromInstallerProperty()
    {
        var definition = CreateMonitoringInstaller();

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "RegistryValue").SingleOrDefault(e =>
            (string?)e.Attribute("Root") == "HKLM" &&
            (string?)e.Attribute("Key") == @"Software\Evotec\TestimoX\Monitoring" &&
            (string?)e.Attribute("Name") == "LicenseKey" &&
            (string?)e.Attribute("Value") == "[LICENSE_KEY]" &&
            (string?)e.Attribute("Type") == "string" &&
            (string?)e.Attribute("KeyPath") == "yes"));
    }

    [Fact]
    public void EmitSource_ThrowsWhenShortcutHasNoTarget()
    {
        var definition = new PowerForgeInstallerDefinition
        {
            Product =
            {
                Name = "DomainDetective",
                Manufacturer = "Evotec",
                Version = "1.0.0",
                UpgradeCode = "{f21d3165-41dd-49a6-a30c-5d3fa3ef6f51}"
            }
        };
        definition.Components.Add(new PowerForgeInstallerShortcutComponent
        {
            Id = "StartMenuShortcutComponent",
            ShortcutId = "StartMenuShortcut",
            Name = "DomainDetective"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("TargetFileId or Target", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenAuthoringIdsAreDuplicated()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Add(new PowerForgeInstallerFolderComponent
        {
            Id = "SmokePayload"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("Duplicate installer component ID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenProductUpgradeCodeIsInvalid()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Product.UpgradeCode = "not-a-guid";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("valid GUID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputIdIsNotWixIdentifier()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "License-Key",
            PropertyName = "LICENSE_KEY",
            Label = "License key"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("valid WiX identifier", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputIdExceedsWixIdentifierLength()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "A" + new string('B', 72),
            PropertyName = "LICENSE_KEY",
            Label = "License key"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("valid WiX identifier", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_AcceptsInputIdAtMaxWixIdentifierLength()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "A" + new string('B', 71),
            PropertyName = "LICENSE_KEY",
            Label = "License key"
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputPropertyIsNotPublicMsiProperty()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "license_key",
            Label = "License key"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("uppercase public MSI property", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputValidationLengthRangeIsInvalid()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            MinLength = 20,
            MaxLength = 10
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("MinLength cannot be greater than MaxLength", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputValidationPatternIsInvalid()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            ValidationPattern = "["
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("valid .NET regular expression", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputDefaultValueViolatesValidationMetadata()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            DefaultValue = "abc",
            MinLength = 5
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("shorter than MinLength", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputDefaultValueExceedsMaxLength()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            DefaultValue = "ABCDE",
            MaxLength = 4
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("longer than MaxLength", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputDefaultValueFailsValidationPattern()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            DefaultValue = "abc",
            ValidationPattern = "^[A-Z0-9-]+$"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("does not match ValidationPattern", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputValidationLengthIsNegative()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            MinLength = -1
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("MinLength must be greater than or equal to 0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenInputValidationMaxLengthIsNegative()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            MaxLength = -1
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("MaxLength must be greater than or equal to 0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenCheckboxUsesInputValidationMetadata()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "RemoveData",
            PropertyName = "REMOVE_DATA",
            Label = "Remove data",
            Kind = PowerForgeInstallerInputKind.Checkbox,
            MinLength = 1
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("validation metadata", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenCheckboxUsesValidationMessageOnly()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "RemoveData",
            PropertyName = "REMOVE_DATA",
            Label = "Remove data",
            Kind = PowerForgeInstallerInputKind.Checkbox,
            ValidationMessage = "Choose whether to remove data."
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("validation metadata", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenComponentFileIdIsNotWixIdentifier()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.OfType<PowerForgeInstallerFileComponent>().Single().FileId = "1SmokePayloadFile";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("valid WiX identifier", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenShortcutWorkingDirectoryIdIsMissing()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Add(new PowerForgeInstallerShortcutComponent
        {
            Id = "StartMenuShortcutComponent",
            ShortcutId = "StartMenuShortcut",
            Name = "Smoke",
            Target = "[INSTALLFOLDER]Smoke.exe",
            WorkingDirectoryId = string.Empty
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("WorkingDirectoryId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenRegistryValuePropertyIsNotPublicMsiProperty()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Add(new PowerForgeInstallerRegistryValueComponent
        {
            Id = "LicenseRegistry",
            Key = @"Software\Evotec\Test",
            Name = "LicenseKey",
            ValueProperty = "license_key"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("uppercase public MSI property", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenDialogReferencesUnknownInput()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ConfigurationDlg",
            Title = "Configuration",
            InputIds = { "MissingInput" }
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("references unknown input", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitSource_ThrowsWhenRegistryValueHasNoValue()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Add(new PowerForgeInstallerRegistryValueComponent
        {
            Id = "LicenseRegistry",
            Key = @"Software\Evotec\Test",
            Name = "LicenseKey"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeWixInstallerSourceEmitter().EmitSource(definition));
        Assert.Contains("Value or ValueProperty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitProjectFile_IncludesRequiredWixExtensionsAndSource()
    {
        var definition = CreateMonitoringInstaller();
        var options = new PowerForgeWixInstallerProjectOptions { SourceFile = "Generated.wxs" };
        options.DefineConstants["PayloadDir"] = @"Artifacts\TestimoX.Monitoring\win-x64";
        options.AdditionalSourceFiles.Add("Harvest.wxs");

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitProjectFile(definition, options);
        var doc = XDocument.Parse(xml);

        Assert.Equal("WixToolset.Sdk/4.0.6", (string?)doc.Root!.Attribute("Sdk"));
        Assert.Equal("false", doc.Descendants("ManagePackageVersionsCentrally").Single().Value);
        Assert.Equal("false", doc.Descendants("EnableDefaultItems").Single().Value);
        Assert.NotNull(doc.Descendants("PackageReference").SingleOrDefault(e =>
            (string?)e.Attribute("Include") == "WixToolset.Util.wixext"));
        Assert.NotNull(doc.Descendants("PackageReference").SingleOrDefault(e =>
            (string?)e.Attribute("Include") == "WixToolset.UI.wixext"));
        Assert.NotNull(doc.Descendants("Compile").SingleOrDefault(e =>
            (string?)e.Attribute("Include") == "Generated.wxs"));
        Assert.NotNull(doc.Descendants("Compile").SingleOrDefault(e =>
            (string?)e.Attribute("Include") == "Harvest.wxs"));
        Assert.Contains("PayloadDir=Artifacts", doc.Descendants("DefineConstants").Single().Value, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitProjectFile_IncludesUtilExtensionForScriptServiceInstall()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.Components.Clear();
        definition.Components.Add(new PowerForgeInstallerServiceComponent
        {
            Id = "ServiceComponent",
            FileId = "ServiceExe",
            Source = "$(var.PayloadDir)\\Service.exe",
            ServiceName = "Contoso.Service",
            DisplayName = "Contoso Service",
            ScriptInstall = new PowerForgeInstallerServiceScriptInstall
            {
                Command = "\"powershell.exe\" -NoP -EP Bypass -File \"[INSTALLFOLDER]Install-Service.ps1\""
            }
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitProjectFile(
            definition,
            new PowerForgeWixInstallerProjectOptions { SourceFile = "Generated.wxs" });
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants("PackageReference").SingleOrDefault(e =>
            (string?)e.Attribute("Include") == "WixToolset.Util.wixext"));
    }

    [Fact]
    public void EmitProjectFile_IncludesUtilAndUiExtensionsForExitLaunch()
    {
        var definition = CreateSimpleFileInstaller(Path.Combine(Path.GetTempPath(), "payload.txt"));
        definition.ExitLaunch = new PowerForgeInstallerExitLaunch
        {
            Text = "Open app",
            Target = "http://127.0.0.1:9000/"
        };

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitProjectFile(
            definition,
            new PowerForgeWixInstallerProjectOptions { SourceFile = "Generated.wxs" });
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants("PackageReference").SingleOrDefault(e =>
            (string?)e.Attribute("Include") == "WixToolset.Util.wixext"));
        Assert.NotNull(doc.Descendants("PackageReference").SingleOrDefault(e =>
            (string?)e.Attribute("Include") == "WixToolset.UI.wixext"));
    }

    [Fact]
    public void PrepareWorkspace_WritesGeneratedSourceAndProject()
    {
        var root = CreateTempDirectory();
        try
        {
            var request = new PowerForgeWixInstallerCompileRequest
            {
                WorkingDirectory = root,
                SourceFileName = "Generated.wxs",
                ProjectFileName = "Generated.wixproj"
            };
            request.DefineConstants["PayloadDir"] = @"Artifacts\TestimoX.Monitoring\win-x64";
            request.AdditionalSourceFiles.Add("Harvest.wxs");

            var workspace = new PowerForgeWixInstallerCompiler()
                .PrepareWorkspace(CreateMonitoringInstaller(), request);

            Assert.True(File.Exists(workspace.SourcePath));
            Assert.True(File.Exists(workspace.ProjectPath));
            Assert.Contains("TestimoX Monitoring", File.ReadAllText(workspace.SourcePath), StringComparison.Ordinal);
            Assert.Contains("WixToolset.Sdk/4.0.6", File.ReadAllText(workspace.ProjectPath), StringComparison.Ordinal);
            Assert.Contains("Harvest.wxs", File.ReadAllText(workspace.ProjectPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CompileAsync_InvokesDotnetBuildAgainstGeneratedProject()
    {
        ProcessRunRequest? capturedRequest = null;
        var runner = new StubProcessRunner(request =>
        {
            capturedRequest = request;
            return new ProcessRunResult(0, "build ok", string.Empty, request.FileName, TimeSpan.FromMilliseconds(1), timedOut: false);
        });
        var root = CreateTempDirectory();
        try
        {
            var request = new PowerForgeWixInstallerCompileRequest
            {
                WorkingDirectory = root,
                SourceFileName = "Generated.wxs",
                ProjectFileName = "Generated.wixproj",
                DotNetExecutable = "dotnet-test",
                Configuration = "Debug",
                NoRestore = true
            };

            var result = await new PowerForgeWixInstallerCompiler(processRunner: runner)
                .CompileAsync(CreateMonitoringInstaller(), request);

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(result.SourcePath));
            Assert.True(File.Exists(result.ProjectPath));
            Assert.NotNull(capturedRequest);
            Assert.Equal("dotnet-test", capturedRequest!.FileName);
            Assert.Equal(root, capturedRequest.WorkingDirectory);
            Assert.Equal(new[] { "build", result.ProjectPath, "-c", "Debug", "--nologo", "--no-restore" }, capturedRequest.Arguments);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CompileAsync_BuildsGeneratedWixProject_WhenOptedIn()
    {
        if (!RunWixCompileTests())
            return;

        var root = CreateTempDirectory();
        try
        {
            var payloadRoot = Path.Combine(root, "payload");
            Directory.CreateDirectory(payloadRoot);
            var payloadFile = Path.Combine(payloadRoot, "PowerForgeSmoke.txt");
            File.WriteAllText(payloadFile, "PowerForge WiX smoke payload");

            var request = new PowerForgeWixInstallerCompileRequest
            {
                WorkingDirectory = Path.Combine(root, "installer"),
                SourceFileName = "Product.wxs",
                ProjectFileName = "PowerForgeSmoke.wixproj",
                Configuration = "Release",
                Timeout = TimeSpan.FromMinutes(3)
            };

            var result = await new PowerForgeWixInstallerCompiler()
                .CompileAsync(CreateSimpleFileInstaller(payloadFile), request);

            Assert.True(
                result.Succeeded,
                $"Generated WiX project failed to compile.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
            Assert.Contains("Build succeeded", result.StdOut, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CompileAsync_BuildsRicherGeneratedWixProject_WhenOptedIn()
    {
        if (!RunWixCompileTests())
            return;

        var root = CreateTempDirectory();
        try
        {
            var payloadRoot = Path.Combine(root, "payload");
            Directory.CreateDirectory(payloadRoot);
            File.WriteAllText(Path.Combine(payloadRoot, "TestimoX.Monitoring.exe"), "fake executable payload");
            File.WriteAllText(Path.Combine(payloadRoot, "TestimoX.Monitoring.example.json"), "{}");

            var request = new PowerForgeWixInstallerCompileRequest
            {
                WorkingDirectory = Path.Combine(root, "installer"),
                SourceFileName = "Product.wxs",
                ProjectFileName = "PowerForgeMonitoringSmoke.wixproj",
                Configuration = "Release",
                Timeout = TimeSpan.FromMinutes(3)
            };

            var result = await new PowerForgeWixInstallerCompiler()
                .CompileAsync(CreateMonitoringCompileInstaller(payloadRoot), request);

            Assert.True(
                result.Succeeded,
                $"Generated rich WiX project failed to compile.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
            Assert.Contains("Build succeeded", result.StdOut, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void DotNetPublishSpec_DeserializesInstallerAuthoringComponentsFromJson()
    {
        var json = """
        {
          "DotNet": {
            "ProjectRoot": ".",
            "Restore": false,
            "Build": false,
            "Runtimes": [ "win-x64" ]
          },
          "Targets": [
            {
              "Name": "monitoring",
              "ProjectPath": "src/Monitoring/Monitoring.csproj",
              "Publish": {
                "Framework": "net10.0",
                "Runtimes": [ "win-x64" ],
                "Style": "Portable"
              }
            }
          ],
          "Installers": [
            {
              "Id": "monitoring.msi",
              "PrepareFromTarget": "monitoring",
              "Harvest": "Auto",
              "Authoring": {
                "Product": {
                  "Name": "TestimoX Monitoring",
                  "Manufacturer": "Evotec",
                  "Version": "1.2.3",
                  "UpgradeCode": "{e3db4c23-7b5f-4967-b2ad-82aee0f7463c}",
                  "Scope": "PerMachine"
                },
                "CompanyFolderName": "TestimoX",
                "InstallDirectoryName": "Monitoring",
                "PayloadComponentGroupId": "ProductFiles",
                "ExitLaunch": {
                  "Text": "Open monitoring",
                  "Target": "http://127.0.0.1:9000/"
                },
                "LicenseAgreement": {
                  "Path": "Installer/TestimoX.Monitoring/License.rtf"
                },
                "Inputs": [
                  {
                    "Id": "LicenseKey",
                    "PropertyName": "LICENSE_KEY",
                    "Label": "License key",
                    "Kind": "LicenseKey",
                    "Secure": true,
                    "Hidden": true,
                    "Required": true,
                    "RequiredMessage": "Enter a license key before continuing.",
                    "MinLength": 16,
                    "MaxLength": 128,
                    "ValidationPattern": "^[A-Za-z0-9-]+$",
                    "ValidationMessage": "Enter a valid license key."
                  },
                  {
                    "Id": "DataDir",
                    "PropertyName": "MONITORINGDATADIR",
                    "Label": "Data folder",
                    "Kind": "FolderPath",
                    "RegistrySearch": {
                      "Id": "MonitoringDataDirSearch",
                      "Root": "HKLM",
                      "Key": "Software\\Evotec\\TestimoX\\Monitoring",
                      "Name": "DataDir",
                      "Type": "raw"
                    }
                  },
                  {
                    "Id": "Preset",
                    "PropertyName": "INIT_PRESET",
                    "Label": "Preset",
                    "Kind": "ComboBox",
                    "DefaultValue": "core",
                    "Choices": [
                      { "Value": "none", "Text": "None" },
                      { "Value": "core", "Text": "Core" }
                    ]
                  }
                ],
                "Dialogs": [
                  {
                    "Id": "ConfigurationDlg",
                    "Title": "Configuration",
                    "InputIds": [ "LicenseKey", "Preset" ],
                    "Actions": [
                      {
                        "Id": "OpenStudio",
                        "Text": "Open Studio",
                        "Target": "http://127.0.0.1:9000/"
                      }
                    ]
                  }
                ],
                "Directories": [
                  {
                    "StandardDirectoryId": "CommonAppDataFolder",
                    "Segments": [
                      { "Id": "ProgramDataCompany", "Name": "TestimoX" },
                      { "Id": "ProgramDataMonitoring", "Name": "Monitoring" }
                    ]
                  }
                ],
                "Components": [
                  {
                    "Type": "Service",
                    "Id": "ServiceComponent",
                    "FileId": "MonitoringExe",
                    "Source": "$(var.PayloadDir)\\TestimoX.Monitoring.exe",
                    "ServiceName": "TestimoX.Monitoring",
                    "DisplayName": "TestimoX Monitoring",
                    "DelayedAutoStart": true
                  },
                  {
                    "Type": "RemoveFolder",
                    "Id": "RemoveProgramData",
                    "DirectoryRefId": "ProgramDataMonitoring",
                    "PropertyName": "ProgramDataMonitoring",
                    "Condition": "REMOVE_DATA=1"
                  },
                  {
                    "Type": "RegistryValue",
                    "Id": "LicenseRegistry",
                    "Key": "Software\\Evotec\\TestimoX\\Monitoring",
                    "Name": "LicenseKey",
                    "ValueProperty": "LICENSE_KEY",
                    "ValueType": "string"
                  }
                ],
                "ExecutableActions": [
                  {
                    "Id": "InitMonitoringConfig",
                    "FileRef": "MonitoringExe",
                    "Arguments": "--init-config --force --config \"[ProgramDataMonitoring]TestimoX.Monitoring.json\" --init-preset [INIT_PRESET] --no-console",
                    "Condition": "INIT_CONFIG=1 AND NOT REMOVE=\"ALL\"",
                    "After": "InstallFiles"
                  }
                ]
              }
            }
          ]
        }
        """;

        var options = CreateJsonOptions();
        var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, options);

        Assert.NotNull(spec);
        var authoring = Assert.Single(spec!.Installers).Authoring;
        Assert.NotNull(authoring);
        Assert.Equal("TestimoX Monitoring", authoring!.Product.Name);
        Assert.Equal("ProductFiles", authoring.PayloadComponentGroupId);
        Assert.NotNull(authoring.ExitLaunch);
        Assert.Equal("http://127.0.0.1:9000/", authoring.ExitLaunch!.Target);
        Assert.NotNull(authoring.LicenseAgreement);
        Assert.Equal("Installer/TestimoX.Monitoring/License.rtf", authoring.LicenseAgreement!.Path);
        Assert.Equal(3, authoring.Inputs.Count);
        var input = authoring.Inputs[0];
        Assert.True(input.Required);
        Assert.Equal("Enter a license key before continuing.", input.RequiredMessage);
        Assert.Equal(16, input.MinLength);
        Assert.Equal(128, input.MaxLength);
        Assert.Equal("^[A-Za-z0-9-]+$", input.ValidationPattern);
        Assert.Equal("Enter a valid license key.", input.ValidationMessage);
        var dataDir = authoring.Inputs[1];
        Assert.Equal("MONITORINGDATADIR", dataDir.PropertyName);
        Assert.NotNull(dataDir.RegistrySearch);
        Assert.Equal("MonitoringDataDirSearch", dataDir.RegistrySearch!.Id);
        var preset = authoring.Inputs[2];
        Assert.Equal(PowerForgeInstallerInputKind.ComboBox, preset.Kind);
        Assert.Equal("core", preset.DefaultValue);
        Assert.Equal(2, preset.Choices.Count);
        Assert.Single(authoring.Dialogs);
        Assert.Single(authoring.Dialogs[0].Actions);
        Assert.Equal("OpenStudio", authoring.Dialogs[0].Actions[0].Id);
        Assert.Single(authoring.Directories);

        var service = Assert.IsType<PowerForgeInstallerServiceComponent>(authoring.Components[0]);
        Assert.Equal("TestimoX.Monitoring", service.ServiceName);
        Assert.True(service.DelayedAutoStart);
        var removeFolder = Assert.IsType<PowerForgeInstallerRemoveFolderComponent>(authoring.Components[1]);
        Assert.Equal("ProgramDataMonitoring", removeFolder.PropertyName);

        var registryValue = Assert.IsType<PowerForgeInstallerRegistryValueComponent>(authoring.Components[2]);
        Assert.Equal("LICENSE_KEY", registryValue.ValueProperty);
        Assert.Equal("string", registryValue.ValueType);
        var executableAction = Assert.Single(authoring.ExecutableActions);
        Assert.Equal("InitMonitoringConfig", executableAction.Id);
        Assert.Equal("MonitoringExe", executableAction.FileRef);
        Assert.Equal("InstallFiles", executableAction.After);

        var serialized = JsonSerializer.Serialize(authoring.Components[2], options);
        Assert.Contains("\"Type\":\"RegistryValue\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"ValueProperty\":\"LICENSE_KEY\"", serialized, StringComparison.Ordinal);
    }

    private static PowerForgeInstallerDefinition CreateMonitoringInstaller()
    {
        var definition = new PowerForgeInstallerDefinition
        {
            Product =
            {
                Name = "TestimoX Monitoring",
                Manufacturer = "Evotec",
                Version = "1.0.0",
                UpgradeCode = "{e3db4c23-7b5f-4967-b2ad-82aee0f7463c}"
            },
            CompanyFolderName = "TestimoX",
            InstallDirectoryName = "TestimoX.Monitoring",
            PayloadComponentGroupId = "ProductFiles"
        };

        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            Secure = true,
            Hidden = true,
            Required = true
        });
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "RemoveData",
            PropertyName = "REMOVE_DATA",
            Label = "Remove data on uninstall",
            Description = "Remove %ProgramData% data on uninstall.",
            Kind = PowerForgeInstallerInputKind.Checkbox,
            DefaultValue = "0"
        });
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "MonitoringDataDirectory",
            PropertyName = "MONITORINGDATADIR",
            Label = "Monitoring data folder",
            Kind = PowerForgeInstallerInputKind.FolderPath,
            RegistrySearch = new PowerForgeInstallerRegistrySearch
            {
                Id = "MonitoringDataDirSearch",
                Root = "HKLM",
                Key = @"Software\Evotec\TestimoX\Monitoring",
                Name = "DataDir",
                Type = "raw"
            }
        });
        var preset = new PowerForgeInstallerInput
        {
            Id = "Preset",
            PropertyName = "INIT_PRESET",
            Label = "Configuration preset",
            Kind = PowerForgeInstallerInputKind.RadioGroup,
            DefaultValue = "none"
        };
        preset.Choices.Add(new PowerForgeInstallerInputChoice { Value = "none", Text = "None" });
        preset.Choices.Add(new PowerForgeInstallerInputChoice { Value = "core", Text = "Core AD" });
        definition.Inputs.Add(preset);
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ConfigurationDlg",
            Title = "Configuration",
            Description = "Choose setup options.",
            InputIds = { "LicenseKey", "RemoveData", "Preset" }
        });

        definition.Directories.Add(new PowerForgeInstallerDirectoryTree
        {
            StandardDirectoryId = "CommonAppDataFolder",
            Segments =
            {
                new PowerForgeInstallerDirectorySegment { Id = "ProgramDataCompany", Name = "TestimoX" },
                new PowerForgeInstallerDirectorySegment { Id = "ProgramDataMonitoring", Name = "Monitoring" }
            }
        });
        definition.Directories.Add(new PowerForgeInstallerDirectoryTree
        {
            StandardDirectoryId = "ProgramMenuFolder",
            Segments =
            {
                new PowerForgeInstallerDirectorySegment { Id = "ApplicationProgramsFolder", Name = "TestimoX" }
            }
        });
        definition.Components.Add(new PowerForgeInstallerFolderComponent
        {
            Id = "ProgramDataRoot",
            DirectoryRefId = "ProgramDataMonitoring",
            Guid = "29ddeae8-5e1e-4928-837c-42d348cd3fba"
        });
        definition.Components.Add(new PowerForgeInstallerFileComponent
        {
            Id = "ProgramDataConfig",
            DirectoryRefId = "ProgramDataMonitoring",
            Guid = "76c2bffc-5d8b-4e2d-a550-a6b425b9d6dd",
            FileId = "MonitoringConfig",
            Source = "$(var.PayloadDir)\\TestimoX.Monitoring.example.json",
            Name = "TestimoX.Monitoring.json",
            Permanent = true,
            NeverOverwrite = true
        });
        definition.Components.Add(new PowerForgeInstallerRemoveFolderComponent
        {
            Id = "RemoveProgramData",
            DirectoryRefId = "ProgramDataMonitoring",
            Guid = "0ba5e0e7-b7ad-4e87-8452-daef8cda22c6",
            Condition = "REMOVE_DATA=1",
            PropertyName = "ProgramDataMonitoring"
        });
        definition.Components.Add(new PowerForgeInstallerServiceComponent
        {
            Id = "ServiceComponent",
            FileId = "MonitoringExe",
            Source = "$(var.PayloadDir)\\TestimoX.Monitoring.exe",
            ServiceName = "TestimoX.Monitoring",
            DisplayName = "TestimoX Monitoring",
            Description = "TestimoX monitoring agent",
            Arguments = "--config \"[ProgramDataMonitoring]TestimoX.Monitoring.json\"",
            DelayedAutoStart = true,
            ControlStart = "install"
        });
        definition.Components.Add(new PowerForgeInstallerShortcutComponent
        {
            Id = "StartMenuShortcutComponent",
            DirectoryRefId = "ApplicationProgramsFolder",
            ShortcutId = "StartMenuShortcut",
            Name = "TestimoX Monitoring",
            Target = "[INSTALLFOLDER]TestimoX.Monitoring.exe",
            RegistryKey = @"Software\Evotec\TestimoX\Monitoring"
        });
        definition.Components.Add(new PowerForgeInstallerRegistryValueComponent
        {
            Id = "LicenseRegistry",
            Key = @"Software\Evotec\TestimoX\Monitoring",
            Name = "LicenseKey",
            ValueProperty = "LICENSE_KEY"
        });

        return definition;
    }

    private static PowerForgeInstallerDefinition CreateMonitoringCompileInstaller(string payloadRoot)
    {
        var definition = CreateMonitoringInstaller();
        definition.PayloadComponentGroupId = null;
        var licensePath = Path.Combine(payloadRoot, "License.rtf");
        File.WriteAllText(licensePath, @"{\rtf1\ansi PowerForge smoke license}");
        definition.LicenseAgreement = new PowerForgeInstallerLicenseAgreement
        {
            Path = licensePath
        };
        var preset = definition.Inputs.Single(input => input.Id == "Preset");
        preset.Kind = PowerForgeInstallerInputKind.ComboBox;
        definition.Dialogs.Single(dialog => dialog.Id == "ConfigurationDlg").Actions.Add(new PowerForgeInstallerDialogAction
        {
            Id = "OpenStudio",
            Text = "Open Studio",
            Target = "http://127.0.0.1:58433/studio"
        });

        foreach (var component in definition.Components)
        {
            if (component is PowerForgeInstallerFileComponent file &&
                string.Equals(file.Id, "ProgramDataConfig", StringComparison.OrdinalIgnoreCase))
            {
                file.Source = Path.Combine(payloadRoot, "TestimoX.Monitoring.example.json");
            }
            else if (component is PowerForgeInstallerServiceComponent service)
            {
                service.Source = Path.Combine(payloadRoot, "TestimoX.Monitoring.exe");
            }
        }

        return definition;
    }

    private static PowerForgeInstallerDefinition CreateSimpleFileInstaller(string payloadFile)
    {
        var definition = new PowerForgeInstallerDefinition
        {
            Product =
            {
                Name = "PowerForge Smoke",
                Manufacturer = "Evotec",
                Version = "1.0.0",
                UpgradeCode = "{6fe5f1d6-9335-4891-8cf5-44142a342cc2}"
            },
            CompanyFolderName = "Evotec",
            InstallDirectoryName = "PowerForge Smoke"
        };
        definition.Components.Add(new PowerForgeInstallerFileComponent
        {
            Id = "SmokePayload",
            FileId = "SmokePayloadFile",
            Source = payloadFile
        });
        return definition;
    }

    private static void AddScriptService(PowerForgeInstallerDefinition definition, string componentId)
    {
        definition.Components.Add(new PowerForgeInstallerServiceComponent
        {
            Id = componentId,
            FileId = componentId + "Exe",
            Source = "$(var.PayloadDir)\\" + componentId + ".exe",
            ServiceName = componentId + ".Service",
            DisplayName = componentId,
            ScriptInstall = new PowerForgeInstallerServiceScriptInstall
            {
                Command = "\"powershell.exe\" -NoP -EP Bypass -File \"[INSTALLFOLDER]Install-Service.ps1\"",
                BackupExistingImagePath = true,
                StopServiceForUpgrade = true
            }
        });
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "powerforge-wix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool RunWixCompileTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("POWERFORGE_RUN_WIX_COMPILE_TESTS"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup for test artefacts.
        }
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_execute(request));
        }
    }
}
