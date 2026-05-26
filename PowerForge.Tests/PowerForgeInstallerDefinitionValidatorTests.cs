using System;

namespace PowerForge.Tests;

public sealed class PowerForgeInstallerDefinitionValidatorTests
{
    [Fact]
    public void Validate_PassesForMinimalValidDefinition()
    {
        PowerForgeInstallerDefinitionValidator.Validate(CreateValidDefinition());
    }

    [Fact]
    public void Validate_ThrowsWhenDefinitionIsNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(null!));

        Assert.Equal("definition", ex.ParamName);
    }

    [Fact]
    public void Validate_AllowsUnderscorePrefixedPublicMsiProperties()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "ConfigPath",
            PropertyName = "_CONFIG_PATH",
            Label = "Configuration path"
        });

        PowerForgeInstallerDefinitionValidator.Validate(definition);
    }

    [Fact]
    public void Validate_RejectsLowercasePublicMsiProperties()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "ConfigPath",
            PropertyName = "_config_path",
            Label = "Configuration path"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("uppercase public MSI property", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PowerForgeRequiredInputDlg")]
    [InlineData("powerforgerequiredinputdlg2")]
    [InlineData("POWERFORGEREQUIREDINPUTDLG2")]
    [InlineData("PowerForgeRequiredInputDlg2")]
    public void Validate_RejectsReservedRequiredInputDialogPrefixCaseInsensitively(string dialogId)
    {
        var definition = CreateValidDefinition();
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = dialogId,
            Title = "Reserved"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsEnabledLicenseAgreementWithoutPath()
    {
        var definition = CreateValidDefinition();
        definition.LicenseAgreement = new PowerForgeInstallerLicenseAgreement();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("LicenseAgreement.Path", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsLicenseAgreementVariableOverrideForGeneratedUi()
    {
        var definition = CreateValidDefinition();
        definition.LicenseAgreement = new PowerForgeInstallerLicenseAgreement
        {
            Path = "License.rtf",
            VariableId = "CustomLicenseRtf"
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("WixUILicenseRtf", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsComboBoxWithoutChoices()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "Preset",
            PropertyName = "PRESET",
            Label = "Preset",
            Kind = PowerForgeInstallerInputKind.ComboBox
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("combo box", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("choices", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsDuplicateAuthoringIdsAfterTrimming()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key"
        });
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = " LicenseKey ",
            PropertyName = "OTHER_LICENSE_KEY",
            Label = "Other license key"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("Duplicate installer input ID", ex.Message, StringComparison.Ordinal);
        Assert.Contains("LicenseKey", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(" LicenseKey ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDuplicateAuthoringIdsCaseInsensitively()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key"
        });
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "licensekey",
            PropertyName = "OTHER_LICENSE_KEY",
            Label = "Other license key"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("Duplicate installer input ID", ex.Message, StringComparison.Ordinal);
        Assert.Contains("LicenseKey", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsDuplicateInputPropertyNames()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key"
        });
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "OtherLicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "Other license key"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("Duplicate installer input property", ex.Message, StringComparison.Ordinal);
        Assert.Contains("LICENSE_KEY", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsDuplicateRegistrySearchIds()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "InstallPath",
            PropertyName = "INSTALL_PATH",
            Label = "Install path",
            RegistrySearch = new PowerForgeInstallerRegistrySearch
            {
                Id = "ExistingInstallPathSearch",
                Root = "HKLM",
                Key = @"Software\Contoso\Product",
                Name = "InstallPath",
                Type = "raw"
            }
        });
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "DataPath",
            PropertyName = "DATA_PATH",
            Label = "Data path",
            RegistrySearch = new PowerForgeInstallerRegistrySearch
            {
                Id = "existinginstallpathsearch",
                Root = "HKLM",
                Key = @"Software\Contoso\Product",
                Name = "DataPath",
                Type = "raw"
            }
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("Duplicate installer registry search ID", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ExistingInstallPathSearch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsMissingDialogIdWithoutNullReference()
    {
        var definition = CreateValidDefinition();
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = null!,
            Title = "Configuration"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("requires Id", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsCompanyFolderDirectoryCollision()
    {
        var definition = CreateValidDefinition();
        definition.Directories.Add(new PowerForgeInstallerDirectoryTree
        {
            StandardDirectoryId = "CommonAppDataFolder",
            Segments =
            {
                new PowerForgeInstallerDirectorySegment { Id = "CompanyFolder", Name = "Evotec" }
            }
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("Duplicate installer directory ID", ex.Message, StringComparison.Ordinal);
        Assert.Contains("CompanyFolder", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDuplicateDialogIds()
    {
        var definition = CreateValidDefinition();
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ConfigurationDlg",
            Title = "Configuration"
        });
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "configurationdlg",
            Title = "Configuration Duplicate"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("Duplicate installer dialog ID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AllowsDialogActionIdsToRepeatAcrossDialogs()
    {
        var definition = CreateValidDefinition();
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ConfigurationDlg",
            Title = "Configuration",
            Actions = { CreateDialogAction("OpenSettings") }
        });
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "DatabaseDlg",
            Title = "Database",
            Actions = { CreateDialogAction("OpenSettings") }
        });

        PowerForgeInstallerDefinitionValidator.Validate(definition);
    }

    [Fact]
    public void Validate_RejectsDuplicateDialogActionIdsInsideSameDialog()
    {
        var definition = CreateValidDefinition();
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ConfigurationDlg",
            Title = "Configuration",
            Actions =
            {
                CreateDialogAction("OpenSettings"),
                CreateDialogAction("opensettings")
            }
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("Duplicate installer dialog 'ConfigurationDlg' action ID", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Next")]
    [InlineData("Title")]
    [InlineData("InstallPath")]
    [InlineData("InstallPathLabel")]
    public void Validate_RejectsDialogActionIdsThatCollideWithGeneratedControls(string actionId)
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "InstallPath",
            PropertyName = "INSTALL_PATH",
            Label = "Install path"
        });
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ConfigurationDlg",
            Title = "Configuration",
            InputIds = { "InstallPath" },
            Actions = { CreateDialogAction(actionId) }
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("collides with a generated dialog control ID", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsValidationMessageWithoutValidationRule()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            ValidationMessage = "Enter a valid license key."
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("ValidationMessage requires MinLength, MaxLength, or ValidationPattern", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AllowsValidationMessageWithValidationRule()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Kind = PowerForgeInstallerInputKind.LicenseKey,
            MinLength = 16,
            ValidationMessage = "Enter a valid license key."
        });

        PowerForgeInstallerDefinitionValidator.Validate(definition);
    }

    [Fact]
    public void Validate_RejectsRequiredMessageWithoutRequiredInput()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            RequiredMessage = "Enter a license key before continuing."
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("RequiredMessage can only be set when Required is true", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AllowsRequiredMessageWithRequiredInput()
    {
        var definition = CreateValidDefinition();
        definition.Inputs.Add(new PowerForgeInstallerInput
        {
            Id = "LicenseKey",
            PropertyName = "LICENSE_KEY",
            Label = "License key",
            Required = true,
            RequiredMessage = "Enter a license key before continuing."
        });

        PowerForgeInstallerDefinitionValidator.Validate(definition);
    }

    [Fact]
    public void Validate_RejectsExecutableActionGeneratedSetDataCollision()
    {
        var definition = CreateValidDefinition();
        definition.ExecutableActions.Add(CreateExecutableAction("Configure"));
        definition.ExecutableActions.Add(CreateExecutableAction("Configure.SetData"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("SetData", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("collides", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsExecutableActionWhenGeneratedSetDataIdIsTooLong()
    {
        var definition = CreateValidDefinition();
        definition.ExecutableActions.Add(CreateExecutableAction("A" + new string('b', 71)));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("generated SetData action ID", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsUnsupportedExecutableActionReturn()
    {
        var definition = CreateValidDefinition();
        var action = CreateExecutableAction("Configure");
        action.Return = "wait";
        definition.ExecutableActions.Add(action);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("Return must be one of", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("ignore")]
    [InlineData("asyncWait")]
    [InlineData("asyncNoWait")]
    public void Validate_AllowsSupportedExecutableActionReturns(string returnMode)
    {
        var definition = CreateValidDefinition();
        var action = CreateExecutableAction("Configure");
        action.Return = returnMode;
        definition.ExecutableActions.Add(action);

        PowerForgeInstallerDefinitionValidator.Validate(definition);
    }

    [Theory]
    [InlineData("Before", "Configure")]
    [InlineData("Before", "Configure.SetData")]
    [InlineData("After", "Configure")]
    [InlineData("After", "Configure.SetData")]
    public void Validate_RejectsSelfReferentialExecutableActionScheduling(string propertyName, string scheduleTarget)
    {
        var definition = CreateValidDefinition();
        var action = CreateExecutableAction("Configure");
        if (propertyName == "Before")
        {
            action.Before = scheduleTarget;
        }
        else
        {
            action.Before = null;
            action.After = scheduleTarget;
        }
        definition.ExecutableActions.Add(action);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("cannot reference itself", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PowerForgeLaunchOnExit")]
    [InlineData("PowerForgeDialogShellExecute")]
    public void Validate_RejectsExecutableActionBuiltInIdCollision(string actionId)
    {
        var definition = CreateValidDefinition();
        definition.ExecutableActions.Add(CreateExecutableAction(actionId));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("generated installer custom action ID", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsExecutableActionServiceScriptIdCollision()
    {
        var definition = CreateValidDefinition();
        definition.Components.Add(new PowerForgeInstallerServiceComponent
        {
            Id = "SyncService",
            DirectoryRefId = "INSTALLFOLDER",
            FileId = "SyncServiceExe",
            Source = "SyncService.exe",
            ServiceName = "SyncService",
            DisplayName = "Sync Service",
            ScriptInstall = new PowerForgeInstallerServiceScriptInstall
            {
                Command = "sc.exe config SyncService binPath= \"[#SyncServiceExe]\"",
                Condition = "NOT REMOVE"
            }
        });
        definition.ExecutableActions.Add(CreateExecutableAction("SyncService.InstallService"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("generated installer custom action ID", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsDialogActionsBeyondRenderedLimit()
    {
        var definition = CreateValidDefinition();
        definition.Dialogs.Add(new PowerForgeInstallerDialog
        {
            Id = "ConfigurationDlg",
            Title = "Configuration",
            Actions =
            {
                CreateDialogAction("OpenOne"),
                CreateDialogAction("OpenTwo"),
                CreateDialogAction("OpenThree"),
                CreateDialogAction("OpenFour")
            }
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeInstallerDefinitionValidator.Validate(definition));

        Assert.Contains("at most 3 actions", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PowerForgeInstallerDefinition CreateValidDefinition()
    {
        var definition = new PowerForgeInstallerDefinition
        {
            Product =
            {
                Name = "PowerForge Installer Smoke",
                Manufacturer = "Evotec",
                Version = "1.0.0",
                UpgradeCode = "{0288b3b9-c153-4b2f-878e-022b4f8abb42}"
            },
            CompanyFolderName = "Evotec",
            InstallDirectoryName = "PowerForge Installer Smoke",
            PayloadComponentGroupId = "ProductFiles"
        };

        definition.Components.Add(new PowerForgeInstallerFolderComponent
        {
            Id = "InstallFolderComponent",
            DirectoryRefId = "INSTALLFOLDER"
        });

        return definition;
    }

    private static PowerForgeInstallerExecutableAction CreateExecutableAction(string id)
        => new()
        {
            Id = id,
            FileRef = "ToolExe",
            Arguments = "configure",
            Condition = "NOT Installed",
            Return = "check",
            Before = "InstallFinalize"
        };

    private static PowerForgeInstallerDialogAction CreateDialogAction(string id)
        => new()
        {
            Id = id,
            Text = id,
            Target = "https://example.test/",
            Condition = "1"
        };
}
