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
        Assert.NotNull(doc.Descendants(Wix + "ServiceInstall").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "TestimoX.Monitoring" &&
            (string?)e.Attribute("Arguments") == "--config \"[ProgramDataMonitoring]TestimoX.Monitoring.json\""));
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
            RegistryKey = @"Software\Evotec\IntelligenceX\Chat"
        });

        var xml = new PowerForgeWixInstallerSourceEmitter().EmitSource(definition);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Descendants(Wix + "StandardDirectory").SingleOrDefault(e =>
            (string?)e.Attribute("Id") == "ProgramMenuFolder"));
        Assert.NotNull(doc.Descendants(Wix + "Shortcut").SingleOrDefault(e =>
            (string?)e.Attribute("Name") == "IntelligenceX Chat" &&
            (string?)e.Attribute("Target") == "[#PrimaryExeFile]"));
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
                  }
                ],
                "Dialogs": [
                  {
                    "Id": "ConfigurationDlg",
                    "Title": "Configuration",
                    "InputIds": [ "LicenseKey" ]
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
        var input = Assert.Single(authoring.Inputs);
        Assert.True(input.Required);
        Assert.Equal("Enter a license key before continuing.", input.RequiredMessage);
        Assert.Equal(16, input.MinLength);
        Assert.Equal(128, input.MaxLength);
        Assert.Equal("^[A-Za-z0-9-]+$", input.ValidationPattern);
        Assert.Equal("Enter a valid license key.", input.ValidationMessage);
        Assert.Single(authoring.Dialogs);
        Assert.Single(authoring.Directories);

        var service = Assert.IsType<PowerForgeInstallerServiceComponent>(authoring.Components[0]);
        Assert.Equal("TestimoX.Monitoring", service.ServiceName);
        Assert.True(service.DelayedAutoStart);
        var removeFolder = Assert.IsType<PowerForgeInstallerRemoveFolderComponent>(authoring.Components[1]);
        Assert.Equal("ProgramDataMonitoring", removeFolder.PropertyName);

        var registryValue = Assert.IsType<PowerForgeInstallerRegistryValueComponent>(authoring.Components[2]);
        Assert.Equal("LICENSE_KEY", registryValue.ValueProperty);
        Assert.Equal("string", registryValue.ValueType);

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
            DelayedAutoStart = true
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
