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
        var requiresModuleStateReadHost = typed.Methods.Any(static method => method.RequiresPowerShellModuleStateRead);
        var requiresModuleStateWriteHost = typed.Methods.Any(static method => method.RequiresPowerShellModuleStateWrite);
        var requiresModuleStateHost = requiresModuleStateReadHost || requiresModuleStateWriteHost;
        if (!requiresCommandRegionHost && !requiresModuleStateHost) return;

        builder.AppendLine($"public static class {GetRuntimeRegionHostTypeName(typed)}");
        builder.AppendLine("{");
        if (requiresModuleStateHost)
        {
            builder.AppendLine("    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Exception, ModuleStateError> ModuleStateErrors = new();");
            builder.AppendLine("    public static void ThrowPowerShellModuleStateError(global::System.Management.Automation.ErrorRecord error)");
            builder.AppendLine("    {");
            builder.AppendLine("        var exception = error.Exception;");
            builder.AppendLine("        ModuleStateErrors.Remove(exception);");
            builder.AppendLine("        ModuleStateErrors.Add(exception, new ModuleStateError(error));");
            builder.AppendLine("        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();");
            builder.AppendLine("        throw exception;");
            builder.AppendLine("    }");
            builder.AppendLine("    public static bool TryTakePowerShellModuleStateError(global::System.Exception exception, out global::System.Management.Automation.ErrorRecord error)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (ModuleStateErrors.TryGetValue(exception, out var state))");
            builder.AppendLine("        {");
            builder.AppendLine("            ModuleStateErrors.Remove(exception);");
            builder.AppendLine("            error = state.Error;");
            builder.AppendLine("            return true;");
            builder.AppendLine("        }");
            builder.AppendLine("        error = null!;");
            builder.AppendLine("        return false;");
            builder.AppendLine("    }");
            builder.AppendLine("    private sealed class ModuleStateError");
            builder.AppendLine("    {");
            builder.AppendLine("        internal ModuleStateError(global::System.Management.Automation.ErrorRecord error) => Error = error;");
            builder.AppendLine("        internal global::System.Management.Automation.ErrorRecord Error { get; }");
            builder.AppendLine("    }");
        }
        if (requiresCommandRegionHost)
        {
            builder.AppendLine("    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Guid, ScriptBlock> Dispatchers = new();");
            builder.AppendLine("    public static void SetDispatcher(global::System.Guid runspaceId, ScriptBlock dispatcher) => Dispatchers[runspaceId] = dispatcher;");
            builder.AppendLine("    public static ScriptBlock? GetDispatcher(global::System.Guid runspaceId) => Dispatchers.TryGetValue(runspaceId, out var dispatcher) ? dispatcher : null;");
            builder.AppendLine("    public static void ClearDispatcher(global::System.Guid runspaceId) => Dispatchers.TryRemove(runspaceId, out _);");
        }
        if (requiresModuleStateReadHost)
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
        if (requiresModuleStateWriteHost)
        {
            builder.AppendLine("    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Guid, global::System.Collections.Concurrent.ConcurrentDictionary<string, ScriptBlock>> ModuleVariableWriters = new();");
            builder.AppendLine("    public static void SetModuleVariableWriter(global::System.Guid runspaceId, string name, ScriptBlock writer)");
            builder.AppendLine("        => ModuleVariableWriters.GetOrAdd(runspaceId, static _ => new(global::System.StringComparer.OrdinalIgnoreCase))[name] = writer;");
            builder.AppendLine("    public static ModuleVariableWriteResult WriteModuleVariable(global::System.Guid runspaceId, string name, object? value)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (!ModuleVariableWriters.TryGetValue(runspaceId, out var writers) || !writers.TryGetValue(name, out var writer))");
            builder.AppendLine("            throw new global::System.InvalidOperationException(\"The parent Hybrid script-module state writer is not registered for this runspace.\");");
            builder.AppendLine("        object? result = writer.InvokeReturnAsIs(new object?[] { value });");
            builder.AppendLine("        if (result is global::System.Management.Automation.PSObject wrapped) result = wrapped.BaseObject;");
            builder.AppendLine("        return result as ModuleVariableWriteResult ?? throw new global::System.InvalidOperationException(\"The parent Hybrid script-module state writer returned an invalid result.\");");
            builder.AppendLine("    }");
            builder.AppendLine("    public static ModuleVariableWriteResult CreateModuleVariableWriteSuccess() => new(null);");
            builder.AppendLine("    public static ModuleVariableWriteResult CreateModuleVariableWriteFailure(global::System.Management.Automation.ErrorRecord error) => new(error);");
            builder.AppendLine("    public static void ClearModuleVariableWriters(global::System.Guid runspaceId) => ModuleVariableWriters.TryRemove(runspaceId, out _);");
            builder.AppendLine("    public sealed class ModuleVariableWriteResult");
            builder.AppendLine("    {");
            builder.AppendLine("        internal ModuleVariableWriteResult(global::System.Management.Automation.ErrorRecord? error) => Error = error;");
            builder.AppendLine("        public global::System.Management.Automation.ErrorRecord? Error { get; }");
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        builder.AppendLine();
    }
}
