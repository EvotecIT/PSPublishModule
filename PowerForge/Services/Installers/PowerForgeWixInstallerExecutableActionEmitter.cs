using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

internal static class PowerForgeWixInstallerExecutableActionEmitter
{
    private static readonly XNamespace WixNamespace = "http://wixtoolset.org/schemas/v4/wxs";

    internal static IEnumerable<XElement> EmitActions(PowerForgeInstallerDefinition definition)
    {
        if (definition.ExecutableActions.Count == 0)
            yield break;

        var sequence = new XElement(WixNamespace + "InstallExecuteSequence");
        foreach (var action in definition.ExecutableActions)
        {
            var setDataId = action.Id + ".SetData";
            yield return new XElement(
                WixNamespace + "CustomAction",
                new XAttribute("Id", setDataId),
                new XAttribute("Property", action.Id),
                new XAttribute("Value", action.Arguments),
                new XAttribute("Execute", "immediate"));

            var customAction = new XElement(
                WixNamespace + "CustomAction",
                new XAttribute("Id", action.Id),
                new XAttribute("FileRef", action.FileRef),
                new XAttribute("ExeCommand", "[CustomActionData]"),
                new XAttribute("Execute", "deferred"),
                new XAttribute("Return", action.Return));
            if (action.ImpersonateNo)
                customAction.Add(new XAttribute("Impersonate", "no"));
            if (action.HideTarget)
                customAction.Add(new XAttribute("HideTarget", "yes"));
            yield return customAction;

            if (!string.IsNullOrWhiteSpace(action.Before))
            {
                sequence.Add(
                    CreateSequenceRow(setDataId, before: action.Id, after: null, action.Condition),
                    CreateSequenceRow(action.Id, before: action.Before!, after: null, action.Condition));
            }
            else
            {
                var after = string.IsNullOrWhiteSpace(action.After) ? "InstallFiles" : action.After!;
                sequence.Add(
                    CreateSequenceRow(setDataId, before: null, after: after, action.Condition),
                    CreateSequenceRow(action.Id, before: null, after: setDataId, action.Condition));
            }
        }

        if (sequence.HasElements)
            yield return sequence;
    }

    private static XElement CreateSequenceRow(string action, string? before, string? after, string condition)
    {
        var element = new XElement(
            WixNamespace + "Custom",
            new XAttribute("Action", action),
            new XAttribute("Condition", string.IsNullOrWhiteSpace(condition) ? "1" : condition));
        if (!string.IsNullOrWhiteSpace(before))
            element.Add(new XAttribute("Before", before!));
        else
            element.Add(new XAttribute("After", string.IsNullOrWhiteSpace(after) ? "InstallFiles" : after!));
        return element;
    }
}
