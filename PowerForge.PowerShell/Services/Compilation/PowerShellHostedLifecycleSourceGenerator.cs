using System.Text;

namespace PowerForge;

/// <summary>Renders the generated cmdlet host for one explicit advanced-function lifecycle contract.</summary>
internal static class PowerShellHostedLifecycleSourceGenerator
{
    internal static void AppendMembers(StringBuilder builder, PowerShellCompiledMethod method)
    {
        var lifecycle = method.Lifecycle!;
        builder.AppendLine("    private SteppablePipeline? __powerForgePipeline;");
        builder.AppendLine("    private readonly object __powerForgeLifecycleGate = new object();");
        builder.AppendLine("    private int __powerForgeCleaned;");
        builder.AppendLine("    private int __powerForgeStopRequested;");
        builder.AppendLine("    private int __powerForgeStopCompletionStarted;");
        builder.AppendLine("    private global::System.Threading.Tasks.Task? __powerForgeCleanupTask;");
        builder.AppendLine("    private global::System.Threading.Tasks.TaskCompletionSource<object?>? __powerForgeCleanupCompletion;");
        builder.AppendLine("    private global::System.Management.Automation.Runspaces.Runspace? __powerForgeRunspace;");
        builder.AppendLine("    private global::System.EventHandler<global::System.Management.Automation.Runspaces.RunspaceAvailabilityEventArgs>? __powerForgeAvailabilityChanged;");
        builder.AppendLine("    private global::System.EventHandler<global::System.Management.Automation.Runspaces.RunspaceStateEventArgs>? __powerForgeRunspaceStateChanged;");
        if (lifecycle.ValueFromPipeline || lifecycle.ValueFromPipelineByPropertyName)
        {
            builder.AppendLine("    private bool __powerForgePipelineInputExplicitlyBound;");
            builder.AppendLine("    private static readonly global::System.Reflection.PropertyInfo? __powerForgeCurrentPipelineObjectProperty = typeof(global::System.Management.Automation.Cmdlet).GetProperty(\"CurrentPipelineObject\", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic);");
            builder.AppendLine("    private static readonly global::System.Reflection.FieldInfo? __powerForgeCurrentPipelineObjectField = typeof(global::System.Management.Automation.Cmdlet).GetField(\"currentObjectInPipeline\", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic);");
        }
        builder.AppendLine();
        if (lifecycle.ValueFromPipeline || lifecycle.ValueFromPipelineByPropertyName)
        {
            builder.AppendLine("    private object? GetCurrentPipelineObject()");
            builder.AppendLine("    {");
            builder.AppendLine("        if (__powerForgeCurrentPipelineObjectProperty is not null) return __powerForgeCurrentPipelineObjectProperty.GetValue(this);");
            builder.AppendLine("        if (__powerForgeCurrentPipelineObjectField is not null) return __powerForgeCurrentPipelineObjectField.GetValue(this);");
            builder.AppendLine("        throw new global::System.PlatformNotSupportedException(\"The active PowerShell host does not expose its current pipeline record to the hosted lifecycle adapter.\");");
            builder.AppendLine("    }");
            builder.AppendLine();
        }
        builder.AppendLine("    protected override void BeginProcessing()");
        builder.AppendLine("    {");
        if (lifecycle.HasClean)
        {
            builder.AppendLine("        var versionTable = SessionState.PSVariable.GetValue(\"PSVersionTable\") as global::System.Collections.IDictionary;");
            builder.AppendLine("        var versionValue = versionTable?[\"PSVersion\"];");
            builder.AppendLine("        var versionType = versionValue?.GetType();");
            builder.AppendLine("        var powerShellMajor = global::System.Convert.ToInt32(versionType?.GetProperty(\"Major\")?.GetValue(versionValue) ?? 0, global::System.Globalization.CultureInfo.InvariantCulture);");
            builder.AppendLine("        var powerShellMinor = global::System.Convert.ToInt32(versionType?.GetProperty(\"Minor\")?.GetValue(versionValue) ?? 0, global::System.Globalization.CultureInfo.InvariantCulture);");
            builder.AppendLine("        var powerShellVersion = new global::System.Version(powerShellMajor, powerShellMinor);");
            builder.Append("        if (powerShellVersion < new global::System.Version(")
                .Append(lifecycle.MinimumPowerShellVersion.Replace('.', ','))
                .AppendLine("))");
            builder.Append("            throw new global::System.PlatformNotSupportedException(")
                .Append(PowerShellCSharpLiteral.QuoteString(
                    $"Function '{method.SourceName}' uses clean and requires PowerShell {lifecycle.MinimumPowerShellVersion} or newer."))
                .AppendLine(");");
        }
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("        __powerForgeRunspace = global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace;");
        builder.AppendLine("        var bound = new global::System.Collections.Generic.Dictionary<string, object?>(global::System.StringComparer.OrdinalIgnoreCase);");
        builder.AppendLine("        foreach (var item in MyInvocation.BoundParameters)");
        builder.AppendLine("            bound[item.Key] = item.Value;");
        if (lifecycle.PipelineParameterNames.Length > 0)
        {
            builder.Append("        __powerForgePipelineInputExplicitlyBound = ")
                .Append(string.Join(" || ", lifecycle.PipelineParameterNames.Select(name =>
                    "MyInvocation.BoundParameters.ContainsKey(" + PowerShellCSharpLiteral.QuoteString(name) + ")")))
                .AppendLine(";");
        }
        var hostedSource = "param($bound)" + Environment.NewLine + "& " + method.HostedLifecycleSource + " @bound";
        builder.Append("            var script = ScriptBlock.Create(")
            .Append(PowerShellCSharpLiteral.QuoteString(hostedSource))
            .AppendLine(");");
        builder.AppendLine("            var pipeline = script.GetSteppablePipeline(CommandOrigin.Internal, new object[] { bound });");
        builder.AppendLine("            lock (__powerForgeLifecycleGate)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (global::System.Threading.Volatile.Read(ref __powerForgeCleaned) != 0 || global::System.Threading.Volatile.Read(ref __powerForgeStopRequested) != 0)");
        builder.AppendLine("                {");
        builder.AppendLine("                    (pipeline as global::System.IDisposable)?.Dispose();");
        builder.AppendLine("                    throw new global::System.OperationCanceledException(\"The hosted PowerShell lifecycle was stopped before begin completed.\");");
        builder.AppendLine("                }");
        builder.AppendLine("                __powerForgePipeline = pipeline;");
        builder.AppendLine("            }");
        builder.AppendLine("            pipeline.Begin(this);");
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            CleanLifecycle();");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        AppendProcessRecord(builder, method, lifecycle);
        builder.AppendLine("    protected override void EndProcessing()");
        builder.AppendLine("    {");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            var pipeline = GetLifecyclePipeline();");
        builder.AppendLine("            if (pipeline is null) return;");
        builder.AppendLine("            WriteLifecycleOutput(pipeline.End());");
        builder.AppendLine("        }");
        builder.AppendLine("        finally { CleanLifecycle(); }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    protected override void StopProcessing() => StopLifecycle();");
        builder.AppendLine();
        builder.AppendLine("    public void Dispose()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (global::System.Threading.Volatile.Read(ref __powerForgeStopRequested) == 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            CleanLifecycle();");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine("        var completion = __powerForgeCleanupCompletion;");
        builder.AppendLine("        var runspace = __powerForgeRunspace;");
        builder.AppendLine("        if (completion is not null && runspace is not null)");
        builder.AppendLine("        {");
        builder.AppendLine("            var state = runspace.RunspaceStateInfo.State;");
        builder.AppendLine("            if (state == global::System.Management.Automation.Runspaces.RunspaceState.Closed || state == global::System.Management.Automation.Runspaces.RunspaceState.Broken)");
        builder.AppendLine("                CompleteStoppedLifecycle(runspace, terminalHost: true, completion);");
        builder.AppendLine("            else if (runspace.RunspaceAvailability == global::System.Management.Automation.Runspaces.RunspaceAvailability.Available)");
        builder.AppendLine("                CompleteStoppedLifecycle(runspace, terminalHost: false, completion);");
        builder.AppendLine("        }");
        builder.AppendLine("        var cleanup = __powerForgeCleanupTask;");
        builder.AppendLine("        if (cleanup?.IsCompleted == true) cleanup.GetAwaiter().GetResult();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private SteppablePipeline? GetLifecyclePipeline()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (global::System.Threading.Volatile.Read(ref __powerForgeCleaned) != 0 || global::System.Threading.Volatile.Read(ref __powerForgeStopRequested) != 0) return null;");
        builder.AppendLine("        lock (__powerForgeLifecycleGate)");
        builder.AppendLine("            return global::System.Threading.Volatile.Read(ref __powerForgeCleaned) == 0 && global::System.Threading.Volatile.Read(ref __powerForgeStopRequested) == 0 ? __powerForgePipeline : null;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void StopLifecycle()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (global::System.Threading.Interlocked.Exchange(ref __powerForgeStopRequested, 1) != 0) return;");
        builder.AppendLine("        var runspace = __powerForgeRunspace;");
        builder.AppendLine("        var completion = new global::System.Threading.Tasks.TaskCompletionSource<object?>(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);");
        builder.AppendLine("        __powerForgeCleanupCompletion = completion;");
        builder.AppendLine("        __powerForgeCleanupTask = completion.Task;");
        builder.AppendLine("        if (runspace is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            CompleteStoppedLifecycle(null, terminalHost: false, completion);");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine("        __powerForgeAvailabilityChanged = (_, args) =>");
        builder.AppendLine("        {");
        builder.AppendLine("            if (args.RunspaceAvailability == global::System.Management.Automation.Runspaces.RunspaceAvailability.Available)");
        builder.AppendLine("                CompleteStoppedLifecycle(runspace, terminalHost: false, completion);");
        builder.AppendLine("        };");
        builder.AppendLine("        __powerForgeRunspaceStateChanged = (_, args) =>");
        builder.AppendLine("        {");
        builder.AppendLine("            var state = args.RunspaceStateInfo.State;");
        builder.AppendLine("            if (state == global::System.Management.Automation.Runspaces.RunspaceState.Closed || state == global::System.Management.Automation.Runspaces.RunspaceState.Broken)");
        builder.AppendLine("                CompleteStoppedLifecycle(runspace, terminalHost: true, completion);");
        builder.AppendLine("        };");
        builder.AppendLine("        runspace.AvailabilityChanged += __powerForgeAvailabilityChanged;");
        builder.AppendLine("        runspace.StateChanged += __powerForgeRunspaceStateChanged;");
        builder.AppendLine("        var currentState = runspace.RunspaceStateInfo.State;");
        builder.AppendLine("        if (currentState == global::System.Management.Automation.Runspaces.RunspaceState.Closed || currentState == global::System.Management.Automation.Runspaces.RunspaceState.Broken)");
        builder.AppendLine("            CompleteStoppedLifecycle(runspace, terminalHost: true, completion);");
        builder.AppendLine("        else if (runspace.RunspaceAvailability == global::System.Management.Automation.Runspaces.RunspaceAvailability.Available)");
        builder.AppendLine("            CompleteStoppedLifecycle(runspace, terminalHost: false, completion);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void CompleteStoppedLifecycle(global::System.Management.Automation.Runspaces.Runspace? runspace, bool terminalHost, global::System.Threading.Tasks.TaskCompletionSource<object?> completion)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (global::System.Threading.Interlocked.CompareExchange(ref __powerForgeStopCompletionStarted, 1, 0) != 0) return;");
        builder.AppendLine("        if (runspace is not null) DetachStoppedLifecycleHandlers(runspace);");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            if (terminalHost)");
        builder.AppendLine("            {");
        builder.AppendLine("                DisposeStoppedLifecycle();");
        builder.AppendLine("            }");
        builder.AppendLine("            else");
        builder.AppendLine("            {");
        builder.AppendLine("                var previous = global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace;");
        builder.AppendLine("                try");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (runspace is not null) global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace = runspace;");
        builder.AppendLine("                    CleanLifecycle();");
        builder.AppendLine("                }");
        builder.AppendLine("                finally { global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace = previous; }");
        builder.AppendLine("            }");
        builder.AppendLine("            completion.TrySetResult(null);");
        builder.AppendLine("        }");
        builder.AppendLine("        catch (global::System.Exception exception) { completion.TrySetException(exception); }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void DetachStoppedLifecycleHandlers(global::System.Management.Automation.Runspaces.Runspace runspace)");
        builder.AppendLine("    {");
        builder.AppendLine("        var availability = __powerForgeAvailabilityChanged;");
        builder.AppendLine("        if (availability is not null) runspace.AvailabilityChanged -= availability;");
        builder.AppendLine("        var state = __powerForgeRunspaceStateChanged;");
        builder.AppendLine("        if (state is not null) runspace.StateChanged -= state;");
        builder.AppendLine("        __powerForgeAvailabilityChanged = null;");
        builder.AppendLine("        __powerForgeRunspaceStateChanged = null;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void DisposeStoppedLifecycle()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (global::System.Threading.Interlocked.CompareExchange(ref __powerForgeCleaned, 1, 0) != 0) return;");
        builder.AppendLine("        SteppablePipeline? pipeline;");
        builder.AppendLine("        lock (__powerForgeLifecycleGate)");
        builder.AppendLine("        {");
        builder.AppendLine("            pipeline = __powerForgePipeline;");
        builder.AppendLine("            __powerForgePipeline = null;");
        builder.AppendLine("        }");
        builder.AppendLine("        try { (pipeline as global::System.IDisposable)?.Dispose(); }");
        builder.AppendLine("        finally { global::System.Threading.Volatile.Write(ref __powerForgeCleaned, 2); }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static void InvokeLifecycleClean(SteppablePipeline pipeline)");
        builder.AppendLine("    {");
        builder.AppendLine("        var clean = typeof(SteppablePipeline).GetMethod(\"Clean\", global::System.Type.EmptyTypes);");
        builder.AppendLine("        clean?.Invoke(pipeline, null);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void WriteLifecycleOutput(global::System.Array? values)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (values is null) return;");
        builder.AppendLine("        foreach (var value in values) WriteObject(value, enumerateCollection: false);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void CleanLifecycle()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (global::System.Threading.Interlocked.CompareExchange(ref __powerForgeCleaned, 1, 0) != 0) return;");
        builder.AppendLine("        SteppablePipeline? pipeline;");
        builder.AppendLine("        lock (__powerForgeLifecycleGate)");
        builder.AppendLine("        {");
        builder.AppendLine("            pipeline = __powerForgePipeline;");
        builder.AppendLine("        }");
        builder.AppendLine("        if (pipeline is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            global::System.Threading.Volatile.Write(ref __powerForgeCleaned, 2);");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            InvokeLifecycleClean(pipeline);");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        builder.AppendLine("            lock (__powerForgeLifecycleGate)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (global::System.Object.ReferenceEquals(__powerForgePipeline, pipeline)) __powerForgePipeline = null;");
        builder.AppendLine("            }");
        builder.AppendLine("            (pipeline as global::System.IDisposable)?.Dispose();");
        builder.AppendLine("            global::System.Threading.Volatile.Write(ref __powerForgeCleaned, 2);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void AppendProcessRecord(
        StringBuilder builder,
        PowerShellCompiledMethod method,
        PowerShellCompilationLifecycleContract lifecycle)
    {
        builder.AppendLine("    protected override void ProcessRecord()");
        builder.AppendLine("    {");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            var pipeline = GetLifecyclePipeline();");
        builder.AppendLine("            if (pipeline is null) return;");
        if (lifecycle.ValueFromPipeline || lifecycle.ValueFromPipelineByPropertyName)
        {
            builder.AppendLine("            if (__powerForgePipelineInputExplicitlyBound)");
            builder.AppendLine("                WriteLifecycleOutput(pipeline.Process());");
            builder.AppendLine("            else");
            builder.AppendLine("                WriteLifecycleOutput(pipeline.Process(GetCurrentPipelineObject()));");
        }
        else if (lifecycle.HasProcess)
        {
            builder.AppendLine("            WriteLifecycleOutput(pipeline.Process());");
        }
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            if (global::System.Threading.Volatile.Read(ref __powerForgeStopRequested) == 0) CleanLifecycle();");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
    }
}
