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
}
