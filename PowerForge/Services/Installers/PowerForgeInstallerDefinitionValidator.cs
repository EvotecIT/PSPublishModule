using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PowerForge;

internal static class PowerForgeInstallerDefinitionValidator
{
    internal const string RequiredInputDialogIdPrefix = "PowerForgeRequiredInputDlg";

    private static readonly Regex WixIdentifierPattern = new(
        "^[A-Za-z_][A-Za-z0-9_.]{0,71}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PublicMsiPropertyPattern = new(
        "^[A-Z_][A-Z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] DialogReservedControlIds =
    {
        "Back",
        "Next",
        "Cancel",
        "Title",
        "Description"
    };

    private static readonly HashSet<string> WixCustomActionReturnValues = new(StringComparer.Ordinal)
    {
        "check",
        "ignore",
        "asyncWait",
        "asyncNoWait"
    };

    private static readonly TimeSpan ValidationPatternMatchTimeout = TimeSpan.FromSeconds(2);

    internal static void Validate(PowerForgeInstallerDefinition definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));

        Require(definition.Product.Name, nameof(definition.Product.Name));
        Require(definition.Product.Manufacturer, nameof(definition.Product.Manufacturer));
        Require(definition.Product.Version, nameof(definition.Product.Version));
        Require(definition.Product.UpgradeCode, nameof(definition.Product.UpgradeCode));
        RequireGuid(definition.Product.UpgradeCode, nameof(definition.Product.UpgradeCode));
        Require(definition.InstallDirectoryId, nameof(definition.InstallDirectoryId));
        RequireWixIdentifier(definition.InstallDirectoryId, nameof(definition.InstallDirectoryId));
        if (definition.UseCompanyFolder)
            Require(definition.CompanyFolderName, nameof(definition.CompanyFolderName));
        Require(definition.InstallDirectoryName, nameof(definition.InstallDirectoryName));
        if (!string.IsNullOrWhiteSpace(definition.PayloadComponentGroupId))
            RequireWixIdentifier(definition.PayloadComponentGroupId, nameof(definition.PayloadComponentGroupId));
        if (definition.ExitLaunch is { Enabled: true } exitLaunch)
        {
            Require(exitLaunch.Text, nameof(definition.ExitLaunch.Text));
            Require(exitLaunch.Target, nameof(definition.ExitLaunch.Target));
            Require(exitLaunch.Condition, nameof(definition.ExitLaunch.Condition));
        }
        if (definition.LicenseAgreement is { Enabled: true } licenseAgreement)
        {
            Require(licenseAgreement.Path, "LicenseAgreement.Path");
            Require(licenseAgreement.VariableId, "LicenseAgreement.VariableId");
            RequireWixIdentifier(licenseAgreement.VariableId, "LicenseAgreement.VariableId");
        }

        EnsureUnique(
            definition.Inputs.Select(input => input.Id),
            "installer input ID");
        EnsureUnique(
            definition.Inputs.Select(input => input.PropertyName),
            "installer input property");
        EnsureUnique(
            definition.Inputs.Select(input => input.RegistrySearch?.Id),
            "installer registry search ID");
        EnsureUnique(
            definition.Dialogs.Select(dialog => dialog.Id),
            "installer dialog ID");
        foreach (var dialog in definition.Dialogs)
        {
            EnsureUnique(
                dialog.Actions.Select(action => action.Id),
                $"installer dialog '{dialog.Id}' action ID");
        }

        EnsureUnique(
            definition.ExecutableActions.Select(action => action.Id),
            "installer executable action ID");
        ValidateExecutableActionIds(definition);
        // Reserve the whole generated-dialog prefix case-insensitively so authored dialogs cannot
        // collide with future generated validation prompts such as PowerForgeRequiredInputDlg2.
        if (definition.Dialogs.Any(dialog =>
            !string.IsNullOrWhiteSpace(dialog.Id) &&
            dialog.Id.StartsWith(RequiredInputDialogIdPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Installer dialog IDs starting with '{RequiredInputDialogIdPrefix}' are reserved for generated required-input validation.");
        }

        var directoryIds = definition.Directories
            .SelectMany(tree => tree.Segments)
            .Select(segment => segment.Id)
            .Concat(new[] { definition.InstallDirectoryId });
        if (definition.UseCompanyFolder)
            directoryIds = directoryIds.Concat(new[] { "CompanyFolder" });
        EnsureUnique(
            directoryIds,
            "installer directory ID");
        EnsureUnique(
            definition.Components.Select(component => component.Id),
            "installer component ID");

        foreach (var input in definition.Inputs)
        {
            Require(input.Id, nameof(input.Id));
            RequireWixIdentifier(input.Id, $"input '{input.Id}' ID");
            Require(input.PropertyName, nameof(input.PropertyName));
            RequirePublicMsiProperty(input.PropertyName, $"input '{input.Id}' property");
            if (!string.IsNullOrWhiteSpace(input.RequiredMessage) && !input.Required)
            {
                throw new InvalidOperationException(
                    $"Input '{input.Id}': RequiredMessage can only be set when Required is true.");
            }

            ValidateInputValidationMetadata(input);
            if (input.RegistrySearch is not null)
            {
                Require(input.RegistrySearch.Id, $"input '{input.Id}' registry search ID");
                RequireWixIdentifier(input.RegistrySearch.Id, $"input '{input.Id}' registry search ID");
                Require(input.RegistrySearch.Root, $"input '{input.Id}' registry search root");
                Require(input.RegistrySearch.Key, $"input '{input.Id}' registry search key");
                Require(input.RegistrySearch.Name, $"input '{input.Id}' registry search name");
                Require(input.RegistrySearch.Type, $"input '{input.Id}' registry search type");
            }

            if (InputKindRequiresChoices(input.Kind) && input.Choices.Count == 0)
                throw new InvalidOperationException($"Input '{input.Id}' is a {DescribeInputKind(input.Kind)} input but has no choices.");
            if (input.Required && InputKindRequiresChoices(input.Kind))
            {
                if (string.IsNullOrWhiteSpace(input.DefaultValue))
                {
                    throw new InvalidOperationException(
                        $"Input '{input.Id}' is a required {DescribeInputKind(input.Kind)} input but has no default value.");
                }

                if (!input.Choices.Any(choice => string.Equals(choice.Value, input.DefaultValue, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"Input '{input.Id}' is a required {DescribeInputKind(input.Kind)} input but its default value does not match any choice.");
                }
            }
        }

        foreach (var dialog in definition.Dialogs)
        {
            Require(dialog.Id, nameof(dialog.Id));
            RequireWixIdentifier(dialog.Id, $"dialog '{dialog.Id}' ID");
            Require(dialog.Title, nameof(dialog.Title));
            if (dialog.Actions.Count > 3)
            {
                throw new InvalidOperationException(
                    $"Dialog '{dialog.Id}' supports at most 3 actions.");
            }

            var dialogInputs = new List<PowerForgeInstallerInput>();
            foreach (var inputId in dialog.InputIds)
            {
                var input = definition.Inputs.FirstOrDefault(item => string.Equals(item.Id, inputId, StringComparison.OrdinalIgnoreCase));
                if (input is null)
                    throw new InvalidOperationException($"Dialog '{dialog.Id}' references unknown input '{inputId}'.");

                dialogInputs.Add(input);
            }

            ValidateDialogActionControlIds(dialog, dialogInputs);
            foreach (var action in dialog.Actions)
            {
                Require(action.Id, $"dialog '{dialog.Id}' action ID");
                RequireWixIdentifier(action.Id, $"dialog '{dialog.Id}' action '{action.Id}' ID");
                Require(action.Text, $"dialog '{dialog.Id}' action '{action.Id}' Text");
                Require(action.Target, $"dialog '{dialog.Id}' action '{action.Id}' Target");
                Require(action.Condition, $"dialog '{dialog.Id}' action '{action.Id}' Condition");
            }
        }

        foreach (var action in definition.ExecutableActions)
        {
            Require(action.Id, "installer executable action ID");
            RequireWixIdentifier(action.Id, $"executable action '{action.Id}' ID");
            Require(action.FileRef, $"executable action '{action.Id}' FileRef");
            RequireWixIdentifier(action.FileRef, $"executable action '{action.Id}' FileRef");
            Require(action.Arguments, $"executable action '{action.Id}' Arguments");
            Require(action.Condition, $"executable action '{action.Id}' Condition");
            Require(action.Return, $"executable action '{action.Id}' Return");
            ValidateExecutableActionReturn(action);
            if (!string.IsNullOrWhiteSpace(action.Before) && !string.IsNullOrWhiteSpace(action.After))
            {
                throw new InvalidOperationException(
                    $"Executable action '{action.Id}' cannot set both Before and After.");
            }

            if (!string.IsNullOrWhiteSpace(action.Before))
                RequireWixIdentifier(action.Before, $"executable action '{action.Id}' Before");
            if (!string.IsNullOrWhiteSpace(action.After))
                RequireWixIdentifier(action.After, $"executable action '{action.Id}' After");
        }

        foreach (var tree in definition.Directories)
        {
            if (string.IsNullOrWhiteSpace(tree.StandardDirectoryId) && string.IsNullOrWhiteSpace(tree.DirectoryRefId))
                throw new InvalidOperationException("Installer directory tree requires StandardDirectoryId or DirectoryRefId.");
            if (!string.IsNullOrWhiteSpace(tree.StandardDirectoryId) && !string.IsNullOrWhiteSpace(tree.DirectoryRefId))
                throw new InvalidOperationException("Installer directory tree cannot set both StandardDirectoryId and DirectoryRefId.");
            if (!string.IsNullOrWhiteSpace(tree.StandardDirectoryId))
                RequireWixIdentifier(tree.StandardDirectoryId, "installer directory tree StandardDirectoryId");
            if (!string.IsNullOrWhiteSpace(tree.DirectoryRefId))
                RequireWixIdentifier(tree.DirectoryRefId, "installer directory tree DirectoryRefId");
            if (tree.Segments.Count == 0)
                throw new InvalidOperationException("Installer directory tree requires at least one segment.");
            foreach (var segment in tree.Segments)
            {
                Require(segment.Id, nameof(segment.Id));
                RequireWixIdentifier(segment.Id, $"directory segment '{segment.Id}' ID");
                Require(segment.Name, nameof(segment.Name));
            }
        }

        foreach (var component in definition.Components)
        {
            Require(component.Id, nameof(component.Id));
            RequireWixIdentifier(component.Id, $"component '{component.Id}' ID");
            Require(component.DirectoryRefId, nameof(component.DirectoryRefId));
            RequireWixIdentifier(component.DirectoryRefId, $"component '{component.Id}' DirectoryRefId");
            if (component is PowerForgeInstallerShortcutComponent shortcut)
            {
                Require(shortcut.ShortcutId, nameof(shortcut.ShortcutId));
                RequireWixIdentifier(shortcut.ShortcutId, $"shortcut component '{shortcut.Id}' ShortcutId");
                Require(shortcut.WorkingDirectoryId, nameof(shortcut.WorkingDirectoryId));
                RequireWixIdentifier(shortcut.WorkingDirectoryId, $"shortcut component '{shortcut.Id}' WorkingDirectoryId");
                if (!string.IsNullOrWhiteSpace(shortcut.TargetFileId))
                    RequireWixIdentifier(shortcut.TargetFileId, $"shortcut component '{shortcut.Id}' TargetFileId");
                if (string.IsNullOrWhiteSpace(shortcut.TargetFileId) && string.IsNullOrWhiteSpace(shortcut.Target))
                    throw new InvalidOperationException(
                        $"Shortcut component '{shortcut.Id}' requires TargetFileId or Target.");
            }
            else if (component is PowerForgeInstallerFileComponent file)
            {
                Require(file.FileId, nameof(file.FileId));
                RequireWixIdentifier(file.FileId, $"file component '{file.Id}' FileId");
                Require(file.Source, nameof(file.Source));
            }
            else if (component is PowerForgeInstallerServiceComponent service)
            {
                Require(service.FileId, nameof(service.FileId));
                RequireWixIdentifier(service.FileId, $"service component '{service.Id}' FileId");
                Require(service.Source, nameof(service.Source));
                Require(service.ServiceName, $"service component '{service.Id}' ServiceName");
                Require(service.DisplayName, $"service component '{service.Id}' DisplayName");
                Require(service.ControlStart, $"service component '{service.Id}' ControlStart");
                Require(service.ControlStop, $"service component '{service.Id}' ControlStop");
                Require(service.ControlRemove, $"service component '{service.Id}' ControlRemove");
                ValidateServiceScriptInstall(service);
            }
            else if (component is PowerForgeInstallerRemoveFolderComponent removeFolder)
            {
                Require(removeFolder.PropertyName, nameof(removeFolder.PropertyName));
                RequireWixIdentifier(removeFolder.PropertyName, $"remove-folder component '{removeFolder.Id}' PropertyName");
            }
            else if (component is PowerForgeInstallerRegistryValueComponent registryValue)
            {
                Require(registryValue.Root, nameof(registryValue.Root));
                Require(registryValue.Key, nameof(registryValue.Key));
                Require(registryValue.Name, nameof(registryValue.Name));
                Require(registryValue.ValueType, nameof(registryValue.ValueType));
                if (!string.IsNullOrWhiteSpace(registryValue.ValueProperty))
                    RequirePublicMsiProperty(registryValue.ValueProperty, $"registry value component '{registryValue.Id}' ValueProperty");
                if (string.IsNullOrWhiteSpace(registryValue.Value) && string.IsNullOrWhiteSpace(registryValue.ValueProperty))
                {
                    throw new InvalidOperationException(
                        $"Registry value component '{registryValue.Id}' requires Value or ValueProperty.");
                }
            }
        }
    }

    private static void ValidateServiceScriptInstall(PowerForgeInstallerServiceComponent service)
    {
        var script = service.ScriptInstall;
        if (script is null)
            return;

        Require(script.Command, $"service component '{service.Id}' ScriptInstall.Command");
        Require(script.Condition, $"service component '{service.Id}' ScriptInstall.Condition");
        if (script.BackupExistingImagePath)
            Require(script.BackupPath, $"service component '{service.Id}' ScriptInstall.BackupPath");
        if (script.StopDelaySeconds < 0)
        {
            throw new InvalidOperationException(
                $"Service component '{service.Id}' ScriptInstall.StopDelaySeconds must be greater than or equal to 0.");
        }
    }

    private static void ValidateExecutableActionIds(PowerForgeInstallerDefinition definition)
    {
        var emittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PowerForgeLaunchOnExit",
            "PowerForgeDialogShellExecute"
        };

        foreach (var service in definition.Components.OfType<PowerForgeInstallerServiceComponent>())
        {
            foreach (var generatedId in PowerForgeWixInstallerServiceScriptEmitter.GetGeneratedActionIds(service))
            {
                emittedIds.Add(generatedId);
            }
        }

        foreach (var action in definition.ExecutableActions)
        {
            if (string.IsNullOrWhiteSpace(action.Id))
                continue;

            var id = action.Id.Trim();
            if (!emittedIds.Add(id))
            {
                throw new InvalidOperationException(
                    $"Executable action '{id}' collides with a generated installer custom action ID.");
            }

            var setDataId = id + ".SetData";
            RequireWixIdentifier(setDataId, $"executable action '{id}' generated SetData action ID");
            if (!emittedIds.Add(setDataId))
            {
                throw new InvalidOperationException(
                    $"Executable action '{id}' generated SetData action '{setDataId}' collides with another installer custom action ID.");
            }
        }
    }

    private static void ValidateDialogActionControlIds(
        PowerForgeInstallerDialog dialog,
        IReadOnlyList<PowerForgeInstallerInput> dialogInputs)
    {
        var emittedControlIds = new HashSet<string>(DialogReservedControlIds, StringComparer.OrdinalIgnoreCase);
        foreach (var input in dialogInputs)
        {
            if (!string.IsNullOrWhiteSpace(input.Id))
            {
                var inputId = input.Id.Trim();
                emittedControlIds.Add(inputId);
                emittedControlIds.Add(inputId + "Label");
            }
        }

        foreach (var action in dialog.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id))
                continue;

            var actionId = action.Id.Trim();
            if (emittedControlIds.Contains(actionId))
            {
                throw new InvalidOperationException(
                    $"Dialog '{dialog.Id}' action ID '{actionId}' collides with a generated dialog control ID.");
            }
        }
    }

    private static void ValidateExecutableActionReturn(PowerForgeInstallerExecutableAction action)
    {
        if (!WixCustomActionReturnValues.Contains(action.Return.Trim()))
        {
            throw new InvalidOperationException(
                $"Executable action '{action.Id}' Return must be one of: check, ignore, asyncWait, asyncNoWait.");
        }
    }

    private static void ValidateInputValidationMetadata(PowerForgeInstallerInput input)
    {
        var hasValidationRule = input.MinLength.HasValue ||
                                input.MaxLength.HasValue ||
                                input.ValidationPattern is not null;
        var hasValidationMetadata = hasValidationRule || !string.IsNullOrWhiteSpace(input.ValidationMessage);
        if (hasValidationMetadata && !SupportsInputValidationMetadata(input.Kind))
        {
            throw new InvalidOperationException(
                $"Input '{input.Id}' validation metadata can only be used with text, password, path, or license-key inputs.");
        }

        if (input.MinLength is < 0)
            throw new InvalidOperationException($"Input '{input.Id}' MinLength must be greater than or equal to 0.");
        if (input.MaxLength is < 0)
            throw new InvalidOperationException($"Input '{input.Id}' MaxLength must be greater than or equal to 0.");
        if (input.MinLength.HasValue &&
            input.MaxLength.HasValue &&
            input.MinLength.Value > input.MaxLength.Value)
        {
            throw new InvalidOperationException($"Input '{input.Id}' MinLength cannot be greater than MaxLength.");
        }

        Regex? validationRegex = null;
        if (input.ValidationPattern is not null)
        {
            if (string.IsNullOrWhiteSpace(input.ValidationPattern))
            {
                throw new InvalidOperationException($"Input '{input.Id}' ValidationPattern cannot be empty.");
            }

            try
            {
                validationRegex = new Regex(
                    input.ValidationPattern,
                    RegexOptions.CultureInvariant,
                    ValidationPatternMatchTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Input '{input.Id}' ValidationPattern must be a valid .NET regular expression.",
                    ex);
            }
        }

        if (!string.IsNullOrWhiteSpace(input.ValidationMessage) && !hasValidationRule)
        {
            throw new InvalidOperationException(
                $"Input '{input.Id}' ValidationMessage requires MinLength, MaxLength, or ValidationPattern.");
        }

        if (input.DefaultValue is not null)
            ValidateInputDefaultValue(input, validationRegex);
    }

    private static bool SupportsInputValidationMetadata(PowerForgeInstallerInputKind kind)
        => kind == PowerForgeInstallerInputKind.Text ||
           kind == PowerForgeInstallerInputKind.Password ||
           kind == PowerForgeInstallerInputKind.FilePath ||
           kind == PowerForgeInstallerInputKind.FolderPath ||
           kind == PowerForgeInstallerInputKind.LicenseKey;

    private static bool InputKindRequiresChoices(PowerForgeInstallerInputKind kind)
        => kind == PowerForgeInstallerInputKind.RadioGroup ||
           kind == PowerForgeInstallerInputKind.ComboBox;

    private static string DescribeInputKind(PowerForgeInstallerInputKind kind)
        => kind == PowerForgeInstallerInputKind.RadioGroup
            ? "radio group"
            : kind == PowerForgeInstallerInputKind.ComboBox
                ? "combo box"
                : kind.ToString();

    private static void ValidateInputDefaultValue(PowerForgeInstallerInput input, Regex? validationRegex)
    {
        var defaultValue = input.DefaultValue!;
        if (input.MinLength.HasValue && defaultValue.Length < input.MinLength.Value)
        {
            throw new InvalidOperationException(
                $"Input '{input.Id}' default value is shorter than MinLength.");
        }

        if (input.MaxLength.HasValue && defaultValue.Length > input.MaxLength.Value)
        {
            throw new InvalidOperationException(
                $"Input '{input.Id}' default value is longer than MaxLength.");
        }

        try
        {
            if (validationRegex is not null && !validationRegex.IsMatch(defaultValue))
            {
                throw new InvalidOperationException(
                    $"Input '{input.Id}' default value does not match ValidationPattern.");
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new InvalidOperationException(
                $"Input '{input.Id}' default value validation timed out while evaluating ValidationPattern.",
                ex);
        }
    }

    private static void EnsureUnique(IEnumerable<string?> values, string label)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var normalized = value!.Trim();
            if (!seen.Add(normalized))
                throw new InvalidOperationException($"Duplicate {label} detected: {normalized}.");
        }
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Installer definition requires {name}.");
    }

    private static void RequireGuid(string? value, string name)
    {
        if (!Guid.TryParse(value, out _))
            throw new InvalidOperationException($"Installer definition requires {name} to be a valid GUID.");
    }

    private static void RequireWixIdentifier(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !WixIdentifierPattern.IsMatch(value))
        {
            throw new InvalidOperationException(
                $"Installer definition requires {name} to be a valid WiX identifier.");
        }
    }

    private static void RequirePublicMsiProperty(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !PublicMsiPropertyPattern.IsMatch(value))
        {
            throw new InvalidOperationException(
                $"Installer definition requires {name} to be an uppercase public MSI property.");
        }
    }
}
