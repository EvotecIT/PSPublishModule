using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Emits WiX v4 source from a PowerForge installer definition.
/// </summary>
public sealed class PowerForgeWixInstallerSourceEmitter
{
    private static readonly XNamespace WixNamespace = "http://wixtoolset.org/schemas/v4/wxs";
    private static readonly XNamespace UtilNamespace = "http://wixtoolset.org/schemas/v4/wxs/util";
    private static readonly XNamespace UiNamespace = "http://wixtoolset.org/schemas/v4/wxs/ui";
    private const int MaxRequiredInputLabelsInMessage = 4;

    /// <summary>
    /// Emits a WiX v4 source document.
    /// </summary>
    /// <param name="definition">Installer definition to emit.</param>
    /// <returns>WiX source XML.</returns>
    public string EmitSource(PowerForgeInstallerDefinition definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        PowerForgeInstallerDefinitionValidator.Validate(definition);

        var needsUi = definition.Dialogs.Count > 0;
        var needsUtil = definition.Components.OfType<PowerForgeInstallerRemoveFolderComponent>().Any();
        var rootAttributes = new List<XAttribute>();
        if (needsUtil)
            rootAttributes.Add(new XAttribute(XNamespace.Xmlns + "util", UtilNamespace.NamespaceName));
        if (needsUi)
            rootAttributes.Add(new XAttribute(XNamespace.Xmlns + "ui", UiNamespace.NamespaceName));

        var package = new XElement(
            WixNamespace + "Package",
            new XAttribute("Name", definition.Product.Name),
            new XAttribute("Manufacturer", definition.Product.Manufacturer),
            new XAttribute("Version", definition.Product.Version),
            new XAttribute("UpgradeCode", definition.Product.UpgradeCode),
            new XAttribute("Scope", ToWixScope(definition.Product.Scope)),
            new XAttribute("Compressed", "yes"),
            new XElement(
                WixNamespace + "MajorUpgrade",
                new XAttribute("DowngradeErrorMessage", definition.Product.DowngradeErrorMessage)),
            new XElement(WixNamespace + "MediaTemplate", new XAttribute("EmbedCab", "yes")));

        EmitProperties(package, definition);
        if (needsUi)
            package.Add(EmitUi(definition));

        package.Add(EmitFeature(definition));

        var root = new XElement(WixNamespace + "Wix", rootAttributes, package);
        root.Add(EmitDirectories(definition));
        root.Add(EmitComponents(definition));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Emits a WiX SDK project file that can compile generated WiX source.
    /// </summary>
    /// <param name="definition">Installer definition used to detect extension references.</param>
    /// <param name="options">Project output options.</param>
    /// <returns>WiX SDK project XML.</returns>
    public string EmitProjectFile(PowerForgeInstallerDefinition definition, PowerForgeWixInstallerProjectOptions options)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SourceFile))
            throw new InvalidOperationException("WiX project options require SourceFile.");

        var sourceFiles = new[] { options.SourceFile }
            .Concat(options.AdditionalSourceFiles)
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(file => file.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => string.Equals(file, options.SourceFile, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var project = new XElement(
            "Project",
            new XAttribute("Sdk", "WixToolset.Sdk/" + options.SdkVersion),
            new XElement(
                "PropertyGroup",
                new XElement("OutputType", "Package"),
                new XElement("EnableDefaultItems", "false"),
                new XElement("Platform", options.Platform)));

        if (options.DefineConstants.Count > 0)
        {
            project.Add(new XElement(
                "PropertyGroup",
                new XElement(
                    "DefineConstants",
                    string.Join(
                        ";",
                        options.DefineConstants
                            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(entry => entry.Key + "=" + entry.Value)))));
        }

        var packageReferences = new List<XElement>();
        if (definition.Components.OfType<PowerForgeInstallerRemoveFolderComponent>().Any())
        {
            packageReferences.Add(new XElement(
                "PackageReference",
                new XAttribute("Include", "WixToolset.Util.wixext"),
                new XAttribute("Version", options.SdkVersion)));
        }

        if (definition.Dialogs.Count > 0)
        {
            packageReferences.Add(new XElement(
                "PackageReference",
                new XAttribute("Include", "WixToolset.UI.wixext"),
                new XAttribute("Version", options.SdkVersion)));
        }

        if (packageReferences.Count > 0)
            project.Add(new XElement("ItemGroup", packageReferences));

        project.Add(new XElement(
            "ItemGroup",
            sourceFiles.Select(file => new XElement("Compile", new XAttribute("Include", file)))));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), project)
            .ToString(SaveOptions.DisableFormatting);
    }

    private static void EmitProperties(XElement package, PowerForgeInstallerDefinition definition)
    {
        package.Add(new XElement(
            WixNamespace + "Property",
            new XAttribute("Id", "WIXUI_INSTALLDIR"),
            new XAttribute("Value", definition.InstallDirectoryId)));

        foreach (var input in definition.Inputs)
        {
            var property = new XElement(
                WixNamespace + "Property",
                new XAttribute("Id", input.PropertyName));

            if (!string.IsNullOrWhiteSpace(input.DefaultValue))
                property.Add(new XAttribute("Value", input.DefaultValue!));
            if (input.Secure)
                property.Add(new XAttribute("Secure", "yes"));
            if (input.Hidden)
                property.Add(new XAttribute("Hidden", "yes"));

            package.Add(property);
        }
    }

    private static XElement EmitUi(PowerForgeInstallerDefinition definition)
    {
        var ui = new XElement(
            WixNamespace + "UI",
            new XElement(UiNamespace + "WixUI", new XAttribute("Id", "WixUI_InstallDir")));

        var dialogs = definition.Dialogs.ToArray();
        var dialogInputs = dialogs.ToDictionary(
            dialog => dialog.Id,
            dialog => ResolveDialogInputs(dialog, definition.Inputs),
            StringComparer.OrdinalIgnoreCase);
        var requiredDialogIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requiredDialogIndex = 0;
        foreach (var dialog in dialogs)
        {
            var requiredInputs = dialogInputs[dialog.Id]
                .Where(input => input.Required)
                .ToArray();
            if (requiredInputs.Length == 0)
                continue;

            var requiredDialogId = BuildRequiredInputDialogId(requiredDialogIndex++);
            requiredDialogIds[dialog.Id] = requiredDialogId;
            ui.Add(EmitRequiredInputDialog(requiredDialogId, requiredInputs));
        }

        for (var i = 0; i < dialogs.Length; i++)
        {
            var previousDialogId = i == 0 ? "InstallDirDlg" : dialogs[i - 1].Id;
            var nextDialogId = i == dialogs.Length - 1 ? "VerifyReadyDlg" : dialogs[i + 1].Id;
            requiredDialogIds.TryGetValue(dialogs[i].Id, out var requiredDialogId);
            ui.Add(EmitDialog(dialogs[i], dialogInputs[dialogs[i].Id], previousDialogId, nextDialogId, requiredDialogId));
        }

        ui.Add(EmitDialogSequence(dialogs));

        return ui;
    }

    private static string BuildRequiredInputDialogId(int index)
    {
        // Keep the first generated ID stable for existing one-dialog installer output; suffix only additional prompts.
        return index == 0
            ? PowerForgeInstallerDefinitionValidator.RequiredInputDialogIdPrefix
            : PowerForgeInstallerDefinitionValidator.RequiredInputDialogIdPrefix + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static XElement EmitRequiredInputDialog(
        string dialogId,
        IReadOnlyList<PowerForgeInstallerInput> requiredInputs)
    {
        return new XElement(
            WixNamespace + "Dialog",
            new XAttribute("Id", dialogId),
            new XAttribute("Width", "300"),
            new XAttribute("Height", "110"),
            new XAttribute("Title", "[ProductName]"),
            new XElement(
                WixNamespace + "Control",
                new XAttribute("Id", "Message"),
                new XAttribute("Type", "Text"),
                new XAttribute("X", "15"),
                new XAttribute("Y", "15"),
                new XAttribute("Width", "270"),
                new XAttribute("Height", "50"),
                new XAttribute("Text", BuildRequiredInputMessage(requiredInputs))),
            new XElement(
                WixNamespace + "Control",
                new XAttribute("Id", "Ok"),
                new XAttribute("Type", "PushButton"),
                new XAttribute("X", "122"),
                new XAttribute("Y", "85"),
                new XAttribute("Width", "56"),
                new XAttribute("Height", "17"),
                new XAttribute("Default", "yes"),
                new XAttribute("Cancel", "yes"),
                new XAttribute("Text", "OK"),
                new XElement(
                    WixNamespace + "Publish",
                    new XAttribute("Event", "EndDialog"),
                    new XAttribute("Value", "Return"),
                    new XAttribute("Condition", "1"))));
    }

    private static string BuildRequiredInputMessage(IReadOnlyList<PowerForgeInstallerInput> requiredInputs)
    {
        var labels = requiredInputs
            .Select(input => string.IsNullOrWhiteSpace(input.Label) ? input.Id : input.Label)
            .ToArray();
        var visibleLabels = labels.Take(MaxRequiredInputLabelsInMessage).ToArray();
        var remaining = labels.Length - visibleLabels.Length;
        var labelText = string.Join("; ", visibleLabels);
        if (remaining > 0)
        {
            labelText += $"; and {remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)} more";
        }

        return labels.Length == 1
            ? $"Fill the required field before continuing: {labelText}."
            : $"Fill required fields before continuing: {labelText}.";
    }

    private static IEnumerable<XElement> EmitDialogSequence(IReadOnlyList<PowerForgeInstallerDialog> dialogs)
    {
        if (dialogs.Count == 0)
            return Array.Empty<XElement>();

        var firstDialogId = dialogs[0].Id;
        var lastDialogId = dialogs[dialogs.Count - 1].Id;
        return new[]
        {
            new XElement(
                WixNamespace + "Publish",
                new XAttribute("Dialog", "InstallDirDlg"),
                new XAttribute("Control", "Next"),
                new XAttribute("Event", "NewDialog"),
                new XAttribute("Value", firstDialogId),
                new XAttribute("Order", "5"),
                new XAttribute("Condition", "WIXUI_DONTVALIDATEPATH OR WIXUI_INSTALLDIR_VALID=\"1\"")),
            new XElement(
                WixNamespace + "Publish",
                new XAttribute("Dialog", "VerifyReadyDlg"),
                new XAttribute("Control", "Back"),
                new XAttribute("Event", "NewDialog"),
                new XAttribute("Value", lastDialogId),
                new XAttribute("Order", "1"),
                new XAttribute("Condition", "NOT Installed"))
        };
    }

    private static XElement EmitDialog(
        PowerForgeInstallerDialog dialog,
        IReadOnlyList<PowerForgeInstallerInput> dialogInputs,
        string previousDialogId,
        string nextDialogId,
        string? requiredDialogId)
    {
        var element = new XElement(
            WixNamespace + "Dialog",
            new XAttribute("Id", dialog.Id),
            new XAttribute("Width", "370"),
            new XAttribute("Height", "270"),
            new XAttribute("Title", dialog.Title));

        element.Add(new XElement(
            WixNamespace + "Control",
            new XAttribute("Id", "Title"),
            new XAttribute("Type", "Text"),
            new XAttribute("X", "15"),
            new XAttribute("Y", "6"),
            new XAttribute("Width", "340"),
            new XAttribute("Height", "15"),
            new XAttribute("Transparent", "yes"),
            new XAttribute("NoPrefix", "yes"),
            new XAttribute("Text", dialog.Title)));

        if (!string.IsNullOrWhiteSpace(dialog.Description))
        {
            element.Add(new XElement(
                WixNamespace + "Control",
                new XAttribute("Id", "Description"),
                new XAttribute("Type", "Text"),
                new XAttribute("X", "20"),
                new XAttribute("Y", "24"),
                new XAttribute("Width", "330"),
                new XAttribute("Height", "30"),
                new XAttribute("Text", dialog.Description!)));
        }

        var y = 60;
        foreach (var input in dialogInputs)
        {
            AddInputControls(element, input, y);
            y += input.Kind == PowerForgeInstallerInputKind.RadioGroup
                ? Math.Max(36, input.Choices.Count * 18 + 24)
                : 42;
        }

        var nextPublishes = BuildNextDialogPublishes(dialogInputs, nextDialogId, requiredDialogId);

        element.Add(
            new XElement(
                WixNamespace + "Control",
                new XAttribute("Id", "Back"),
                new XAttribute("Type", "PushButton"),
                new XAttribute("X", "180"),
                new XAttribute("Y", "243"),
                new XAttribute("Width", "56"),
                new XAttribute("Height", "17"),
                new XAttribute("Text", "&Back"),
                new XElement(
                    WixNamespace + "Publish",
                    new XAttribute("Event", "NewDialog"),
                    new XAttribute("Value", previousDialogId),
                    new XAttribute("Condition", "1"))),
            new XElement(
                WixNamespace + "Control",
                new XAttribute("Id", "Next"),
                new XAttribute("Type", "PushButton"),
                new XAttribute("X", "236"),
                new XAttribute("Y", "243"),
                new XAttribute("Width", "56"),
                new XAttribute("Height", "17"),
                new XAttribute("Default", "yes"),
                new XAttribute("Text", "&Next"),
                nextPublishes),
            new XElement(
                WixNamespace + "Control",
                new XAttribute("Id", "Cancel"),
                new XAttribute("Type", "PushButton"),
                new XAttribute("X", "304"),
                new XAttribute("Y", "243"),
                new XAttribute("Width", "56"),
                new XAttribute("Height", "17"),
                new XAttribute("Cancel", "yes"),
                new XAttribute("Text", "Cancel"),
                new XElement(
                    WixNamespace + "Publish",
                    new XAttribute("Event", "SpawnDialog"),
                    new XAttribute("Value", "CancelDlg"),
                    new XAttribute("Condition", "1"))));

        return element;
    }

    private static IEnumerable<XElement> BuildNextDialogPublishes(
        IReadOnlyList<PowerForgeInstallerInput> dialogInputs,
        string nextDialogId,
        string? requiredDialogId)
    {
        var requiredInputs = dialogInputs
            .Where(input => input.Required)
            .ToArray();

        if (requiredInputs.Length == 0)
        {
            return new[]
            {
                new XElement(
                    WixNamespace + "Publish",
                    new XAttribute("Event", "NewDialog"),
                    new XAttribute("Value", nextDialogId),
                    new XAttribute("Condition", "1"))
            };
        }

        var missingCondition = string.Join(" OR ", requiredInputs.Select(BuildRequiredInputMissingCondition));
        var satisfiedCondition = string.Join(" AND ", requiredInputs.Select(BuildRequiredInputPresentCondition));
        if (requiredDialogId is null)
        {
            throw new InvalidOperationException("A required-input dialog ID is required when a dialog has required inputs.");
        }

        return new[]
        {
            new XElement(
                WixNamespace + "Publish",
                new XAttribute("Event", "SpawnDialog"),
                new XAttribute("Value", requiredDialogId),
                new XAttribute("Order", "1"),
                new XAttribute("Condition", missingCondition)),
            new XElement(
                WixNamespace + "Publish",
                new XAttribute("Event", "NewDialog"),
                new XAttribute("Value", nextDialogId),
                new XAttribute("Order", "2"),
                new XAttribute("Condition", satisfiedCondition))
        };
    }

    private static string BuildRequiredInputPresentCondition(PowerForgeInstallerInput input)
    {
        return $"{input.PropertyName} <> \"\"";
    }

    private static string BuildRequiredInputMissingCondition(PowerForgeInstallerInput input)
    {
        return $"{input.PropertyName} = \"\"";
    }

    private static IReadOnlyList<PowerForgeInstallerInput> ResolveDialogInputs(
        PowerForgeInstallerDialog dialog,
        IReadOnlyList<PowerForgeInstallerInput> inputs)
    {
        var dialogInputs = new List<PowerForgeInstallerInput>();
        foreach (var inputId in dialog.InputIds)
        {
            var input = inputs.FirstOrDefault(i => string.Equals(i.Id, inputId, StringComparison.OrdinalIgnoreCase));
            if (input is null)
                throw new InvalidOperationException($"Dialog '{dialog.Id}' references unknown input '{inputId}'.");

            dialogInputs.Add(input);
        }

        return dialogInputs;
    }

    private static void AddInputControls(XElement dialog, PowerForgeInstallerInput input, int y)
    {
        dialog.Add(new XElement(
            WixNamespace + "Control",
            new XAttribute("Id", input.Id + "Label"),
            new XAttribute("Type", "Text"),
            new XAttribute("X", "20"),
            new XAttribute("Y", y.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new XAttribute("Width", "330"),
            new XAttribute("Height", "15"),
            new XAttribute("Text", input.Label)));

        if (input.Kind == PowerForgeInstallerInputKind.Checkbox)
        {
            dialog.Add(new XElement(
                WixNamespace + "Control",
                new XAttribute("Id", input.Id),
                new XAttribute("Type", "CheckBox"),
                new XAttribute("X", "20"),
                new XAttribute("Y", (y + 18).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("Width", "330"),
                new XAttribute("Height", "18"),
                new XAttribute("Property", input.PropertyName),
                new XAttribute("CheckBoxValue", "1"),
                new XAttribute("Text", string.IsNullOrWhiteSpace(input.Description) ? input.Label : input.Description!)));
            return;
        }

        if (input.Kind == PowerForgeInstallerInputKind.RadioGroup)
        {
            var control = new XElement(
                WixNamespace + "Control",
                new XAttribute("Id", input.Id),
                new XAttribute("Type", "RadioButtonGroup"),
                new XAttribute("X", "30"),
                new XAttribute("Y", (y + 18).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("Width", "320"),
                new XAttribute("Height", Math.Max(18, input.Choices.Count * 18).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("Property", input.PropertyName));
            var group = new XElement(WixNamespace + "RadioButtonGroup", new XAttribute("Property", input.PropertyName));
            for (var i = 0; i < input.Choices.Count; i++)
            {
                var choice = input.Choices[i];
                group.Add(new XElement(
                    WixNamespace + "RadioButton",
                    new XAttribute("Value", choice.Value),
                    new XAttribute("Text", choice.Text),
                    new XAttribute("X", "0"),
                    new XAttribute("Y", (i * 18).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new XAttribute("Width", "320"),
                    new XAttribute("Height", "16")));
            }

            control.Add(group);
            dialog.Add(control);
            return;
        }

        var edit = new XElement(
            WixNamespace + "Control",
            new XAttribute("Id", input.Id),
            new XAttribute("Type", "Edit"),
            new XAttribute("X", "20"),
            new XAttribute("Y", (y + 18).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new XAttribute("Width", "330"),
            new XAttribute("Height", "18"),
            new XAttribute("Property", input.PropertyName));

        if (input.Kind == PowerForgeInstallerInputKind.Password || input.Kind == PowerForgeInstallerInputKind.LicenseKey)
            edit.Add(new XAttribute("Password", "yes"));

        dialog.Add(edit);
    }

    private static XElement EmitFeature(PowerForgeInstallerDefinition definition)
    {
        var feature = new XElement(
            WixNamespace + "Feature",
            new XAttribute("Id", "ProductFeature"),
            new XAttribute("Title", definition.Product.Name),
            new XAttribute("Level", "1"));

        foreach (var component in definition.Components)
            feature.Add(new XElement(WixNamespace + "ComponentRef", new XAttribute("Id", component.Id)));

        if (!string.IsNullOrWhiteSpace(definition.PayloadComponentGroupId))
        {
            feature.Add(new XElement(
                WixNamespace + "ComponentGroupRef",
                new XAttribute("Id", definition.PayloadComponentGroupId!)));
        }

        return feature;
    }

    private static object[] EmitDirectories(PowerForgeInstallerDefinition definition)
    {
        var installDirectory = new XElement(
            WixNamespace + "Directory",
            new XAttribute("Id", definition.InstallDirectoryId),
            new XAttribute("Name", definition.InstallDirectoryName));
        XElement programFilesChild = definition.UseCompanyFolder
            ? new XElement(
                WixNamespace + "Directory",
                new XAttribute("Id", "CompanyFolder"),
                new XAttribute("Name", definition.CompanyFolderName),
                installDirectory)
            : installDirectory;

        var fragments = new List<object>
        {
            new XElement(
                WixNamespace + "Fragment",
                new XElement(
                    WixNamespace + "StandardDirectory",
                    new XAttribute("Id", "ProgramFiles64Folder"),
                    programFilesChild))
        };

        foreach (var tree in definition.Directories)
        {
            XElement? current = null;
            foreach (var segment in tree.Segments.AsEnumerable().Reverse())
            {
                current = current is null
                    ? new XElement(
                        WixNamespace + "Directory",
                        new XAttribute("Id", segment.Id),
                        new XAttribute("Name", segment.Name))
                    : new XElement(
                        WixNamespace + "Directory",
                        new XAttribute("Id", segment.Id),
                        new XAttribute("Name", segment.Name),
                        current);
            }

            if (current is not null)
            {
                var root = string.IsNullOrWhiteSpace(tree.DirectoryRefId)
                    ? new XElement(
                        WixNamespace + "StandardDirectory",
                        new XAttribute("Id", tree.StandardDirectoryId),
                        current)
                    : new XElement(
                        WixNamespace + "DirectoryRef",
                        new XAttribute("Id", tree.DirectoryRefId!),
                        current);

                fragments.Add(new XElement(WixNamespace + "Fragment", root));
            }
        }

        return fragments.ToArray();
    }

    private static XElement EmitComponents(PowerForgeInstallerDefinition definition)
    {
        var fragment = new XElement(WixNamespace + "Fragment");
        foreach (var group in definition.Components.GroupBy(c => c.DirectoryRefId, StringComparer.OrdinalIgnoreCase))
        {
            var directoryRef = new XElement(WixNamespace + "DirectoryRef", new XAttribute("Id", group.Key));
            foreach (var component in group)
                directoryRef.Add(EmitComponent(component));
            fragment.Add(directoryRef);
        }

        return fragment;
    }

    private static XElement EmitComponent(PowerForgeInstallerComponent component)
    {
        if (component is PowerForgeInstallerFileComponent file)
            return EmitFileComponent(file);
        if (component is PowerForgeInstallerFolderComponent folder)
            return EmitFolderComponent(folder);
        if (component is PowerForgeInstallerRemoveFolderComponent removeFolder)
            return EmitRemoveFolderComponent(removeFolder);
        if (component is PowerForgeInstallerServiceComponent service)
            return EmitServiceComponent(service);
        if (component is PowerForgeInstallerRegistryValueComponent registryValue)
            return EmitRegistryValueComponent(registryValue);
        if (component is PowerForgeInstallerShortcutComponent shortcut)
            return EmitShortcutComponent(shortcut);

        throw new NotSupportedException($"Unsupported installer component type '{component.GetType().FullName}'.");
    }

    private static XElement EmitFileComponent(PowerForgeInstallerFileComponent file)
    {
        var component = CreateComponent(file);
        if (file.Permanent)
            component.Add(new XAttribute("Permanent", "yes"));
        if (file.NeverOverwrite)
            component.Add(new XAttribute("NeverOverwrite", "yes"));

        var fileElement = new XElement(
            WixNamespace + "File",
            new XAttribute("Id", file.FileId),
            new XAttribute("Source", file.Source));
        if (!string.IsNullOrWhiteSpace(file.Name))
            fileElement.Add(new XAttribute("Name", file.Name!));
        if (file.KeyPath)
            fileElement.Add(new XAttribute("KeyPath", "yes"));
        component.Add(fileElement);
        return component;
    }

    private static XElement EmitFolderComponent(PowerForgeInstallerFolderComponent folder)
    {
        var component = CreateComponent(folder);
        if (folder.Permanent)
            component.Add(new XAttribute("Permanent", "yes"));
        component.Add(new XElement(WixNamespace + "CreateFolder"));
        return component;
    }

    private static XElement EmitRemoveFolderComponent(PowerForgeInstallerRemoveFolderComponent removeFolder)
    {
        var component = CreateComponent(removeFolder);
        if (!string.IsNullOrWhiteSpace(removeFolder.Condition))
            component.Add(new XAttribute("Condition", removeFolder.Condition));
        component.Add(new XElement(
            UtilNamespace + "RemoveFolderEx",
            new XAttribute("On", "uninstall"),
            new XAttribute("Property", removeFolder.PropertyName)));
        return component;
    }

    private static XElement EmitServiceComponent(PowerForgeInstallerServiceComponent service)
    {
        var component = CreateComponent(service);
        component.Add(new XElement(
            WixNamespace + "File",
            new XAttribute("Id", service.FileId),
            new XAttribute("Source", service.Source),
            new XAttribute("KeyPath", "yes")));

        var serviceInstall = new XElement(
            WixNamespace + "ServiceInstall",
            new XAttribute("Id", service.Id + "Install"),
            new XAttribute("Name", service.ServiceName),
            new XAttribute("DisplayName", service.DisplayName),
            new XAttribute("Start", service.Start),
            new XAttribute("Type", "ownProcess"),
            new XAttribute("ErrorControl", "normal"),
            new XAttribute("Vital", "yes"),
            new XAttribute("Account", service.Account));
        if (!string.IsNullOrWhiteSpace(service.Description))
            serviceInstall.Add(new XAttribute("Description", service.Description!));
        if (!string.IsNullOrWhiteSpace(service.Arguments))
            serviceInstall.Add(new XAttribute("Arguments", service.Arguments!));
        component.Add(serviceInstall);

        if (service.DelayedAutoStart)
        {
            component.Add(new XElement(
                WixNamespace + "RegistryValue",
                new XAttribute("Root", "HKLM"),
                new XAttribute("Key", @"SYSTEM\CurrentControlSet\Services\" + service.ServiceName),
                new XAttribute("Name", "DelayedAutoStart"),
                new XAttribute("Type", "integer"),
                new XAttribute("Value", "1")));
        }

        component.Add(new XElement(
            WixNamespace + "ServiceControl",
            new XAttribute("Id", service.Id + "Control"),
            new XAttribute("Name", service.ServiceName),
            new XAttribute("Start", "none"),
            new XAttribute("Stop", "both"),
            new XAttribute("Remove", "both"),
            new XAttribute("Wait", "yes")));

        return component;
    }

    private static XElement EmitRegistryValueComponent(PowerForgeInstallerRegistryValueComponent registryValue)
    {
        var component = CreateComponent(registryValue);
        var value = !string.IsNullOrWhiteSpace(registryValue.ValueProperty)
            ? "[" + registryValue.ValueProperty!.Trim() + "]"
            : registryValue.Value;
        var element = new XElement(
            WixNamespace + "RegistryValue",
            new XAttribute("Root", registryValue.Root),
            new XAttribute("Key", registryValue.Key),
            new XAttribute("Name", registryValue.Name),
            new XAttribute("Type", registryValue.ValueType),
            new XAttribute("Value", value ?? string.Empty));
        if (registryValue.KeyPath)
            element.Add(new XAttribute("KeyPath", "yes"));
        component.Add(element);
        return component;
    }

    private static XElement EmitShortcutComponent(PowerForgeInstallerShortcutComponent shortcut)
    {
        var component = CreateComponent(shortcut);
        var target = !string.IsNullOrWhiteSpace(shortcut.Target)
            ? shortcut.Target!
            : "[#" + shortcut.TargetFileId + "]";
        component.Add(new XElement(
            WixNamespace + "Shortcut",
            new XAttribute("Id", shortcut.ShortcutId),
            new XAttribute("Name", shortcut.Name),
            new XAttribute("Target", target),
            new XAttribute("WorkingDirectory", shortcut.WorkingDirectoryId)));
        component.Add(new XElement(
            WixNamespace + "RemoveFolder",
            new XAttribute("Id", shortcut.Id + "RemoveFolder"),
            new XAttribute("On", "uninstall")));
        component.Add(new XElement(
            WixNamespace + "RegistryValue",
            new XAttribute("Root", "HKCU"),
            new XAttribute("Key", shortcut.RegistryKey),
            new XAttribute("Name", shortcut.RegistryValueName),
            new XAttribute("Type", "integer"),
            new XAttribute("Value", "1"),
            new XAttribute("KeyPath", "yes")));
        return component;
    }

    private static XElement CreateComponent(PowerForgeInstallerComponent component)
    {
        return new XElement(
            WixNamespace + "Component",
            new XAttribute("Id", component.Id),
            new XAttribute("Guid", component.Guid));
    }

    private static string ToWixScope(PowerForgeInstallerScope scope)
    {
        return scope == PowerForgeInstallerScope.PerUser ? "perUser" : "perMachine";
    }
}
