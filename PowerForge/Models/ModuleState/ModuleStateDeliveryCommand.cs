namespace PowerForge;

internal sealed class ModuleStateDeliveryCommand
{
    internal ModuleStateDeliveryCommand(
        ModuleStatePlanActionKind actionKind,
        string moduleName,
        string versionPolicy,
        bool isRepair,
        bool force,
        string commandName,
        string[] arguments,
        string commandText)
    {
        ActionKind = actionKind;
        ModuleName = moduleName;
        VersionPolicy = versionPolicy;
        IsRepair = isRepair;
        Force = force;
        CommandName = commandName;
        Arguments = arguments;
        CommandText = commandText;
    }

    internal ModuleStatePlanActionKind ActionKind { get; }

    internal string ModuleName { get; }

    internal string VersionPolicy { get; }

    internal bool IsRepair { get; }

    internal bool Force { get; }

    internal string CommandName { get; }

    internal string[] Arguments { get; }

    internal string CommandText { get; }
}
