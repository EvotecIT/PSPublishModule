namespace PowerForge;

internal sealed class ModuleStateDeliveryCommand
{
    internal ModuleStateDeliveryCommand(
        ModuleStatePlanActionKind actionKind,
        string moduleName,
        string versionPolicy,
        bool isRepair,
        string commandName,
        string[] arguments,
        string commandText)
    {
        ActionKind = actionKind;
        ModuleName = moduleName;
        VersionPolicy = versionPolicy;
        IsRepair = isRepair;
        CommandName = commandName;
        Arguments = arguments;
        CommandText = commandText;
    }

    internal ModuleStatePlanActionKind ActionKind { get; }

    internal string ModuleName { get; }

    internal string VersionPolicy { get; }

    internal bool IsRepair { get; }

    internal string CommandName { get; }

    internal string[] Arguments { get; }

    internal string CommandText { get; }
}
