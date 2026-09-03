using System.Text;

namespace PowerForge;

internal static partial class PowerShellBinaryCmdletSourceGenerator
{
    private static void AppendRuntimeHost(
        StringBuilder builder,
        PowerShellTypedCompilationResult typed,
        IReadOnlyCollection<CmdletDescriptor> cmdlets)
    {
        var requiresCommandRegionHost = cmdlets.Any(static cmdlet => cmdlet.Method.RequiresPowerShellCommandRegions);
        var requiresModuleStateHost = typed.Methods.Any(static method => method.RequiresPowerShellModuleState);
        if (!requiresCommandRegionHost && !requiresModuleStateHost) return;

        builder.AppendLine($"public static class {GetRuntimeRegionHostTypeName(typed)}");
        builder.AppendLine("{");
        if (requiresCommandRegionHost)
        {
            builder.AppendLine("    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Guid, ScriptBlock> Dispatchers = new();");
            builder.AppendLine("    public static void SetDispatcher(global::System.Guid runspaceId, ScriptBlock dispatcher) => Dispatchers[runspaceId] = dispatcher;");
            builder.AppendLine("    public static ScriptBlock? GetDispatcher(global::System.Guid runspaceId) => Dispatchers.TryGetValue(runspaceId, out var dispatcher) ? dispatcher : null;");
            builder.AppendLine("    public static void ClearDispatcher(global::System.Guid runspaceId) => Dispatchers.TryRemove(runspaceId, out _);");
        }
        if (requiresModuleStateHost)
        {
            builder.AppendLine("    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Guid, global::System.Collections.Concurrent.ConcurrentDictionary<string, ScriptBlock>> ModuleVariableReaders = new();");
            builder.AppendLine("    public static void SetModuleVariableReader(global::System.Guid runspaceId, string name, ScriptBlock reader)");
            builder.AppendLine("        => ModuleVariableReaders.GetOrAdd(runspaceId, static _ => new(global::System.StringComparer.OrdinalIgnoreCase))[name] = reader;");
            builder.AppendLine("    public static ModuleVariableReadResult ReadModuleVariable(global::System.Guid runspaceId, string name)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (!ModuleVariableReaders.TryGetValue(runspaceId, out var readers) || !readers.TryGetValue(name, out var reader))");
            builder.AppendLine("            throw new global::System.InvalidOperationException(\"The parent Hybrid script-module state reader is not registered for this runspace.\");");
            builder.AppendLine("        object? value = reader.InvokeReturnAsIs();");
            builder.AppendLine("        if (value is global::System.Management.Automation.PSObject wrapped) value = wrapped.BaseObject;");
            builder.AppendLine("        return value as ModuleVariableReadResult ?? throw new global::System.InvalidOperationException(\"The parent Hybrid script-module state reader returned an invalid result.\");");
            builder.AppendLine("    }");
            builder.AppendLine("    public static ModuleVariableReadResult CreateModuleVariableReadSuccess(object? value) => new(value, null);");
            builder.AppendLine("    public static ModuleVariableReadResult CreateModuleVariableReadFailure(global::System.Management.Automation.ErrorRecord error) => new(null, error);");
            builder.AppendLine("    public static void ClearModuleVariableReaders(global::System.Guid runspaceId) => ModuleVariableReaders.TryRemove(runspaceId, out _);");
            builder.AppendLine("    public sealed class ModuleVariableReadResult");
            builder.AppendLine("    {");
            builder.AppendLine("        internal ModuleVariableReadResult(object? value, global::System.Management.Automation.ErrorRecord? error) { Value = value; Error = error; }");
            builder.AppendLine("        public object? Value { get; }");
            builder.AppendLine("        public global::System.Management.Automation.ErrorRecord? Error { get; }");
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        builder.AppendLine();
    }
}
