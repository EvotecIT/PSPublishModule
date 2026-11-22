namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.ExceptionServices;

    /// <summary>Owns stream-variable registrations for one generated function clause.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public sealed class PowerShellCommandVariableScope : IDisposable
    {
        private static readonly Type RuntimeType = typeof(PSCmdlet).Assembly.GetType("System.Management.Automation.MshCommandRuntime", true)!;
        private static readonly MethodInfo RemoveLists = RequireMethod("RemoveVariableListsInPipe");
        private static readonly MethodInfo SetLists = RequireMethod("SetVariableListsInPipe");
        private readonly object _runtime;
        private bool _disposed;

        private PowerShellCommandVariableScope(object runtime)
        {
            _runtime = runtime;
            Invoke(RemoveLists, runtime);
            Invoke(SetLists, runtime);
        }

        internal static void PrepareInputBinding(PSCmdlet cmdlet)
            => Invoke(RemoveLists, RequireRuntime(cmdlet));

        internal static PowerShellCommandVariableScope EnterClause(PSCmdlet cmdlet)
            => new PowerShellCommandVariableScope(RequireRuntime(cmdlet));

        /// <summary>Removes only this command's registrations when its clause finishes or unwinds.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Invoke(RemoveLists, _runtime);
        }

        private static object RequireRuntime(PSCmdlet cmdlet)
        {
            if (cmdlet is null) throw new ArgumentNullException(nameof(cmdlet));
            if (!RuntimeType.IsInstanceOfType(cmdlet.CommandRuntime))
                throw new NotSupportedException("Generated function stream-variable capture requires the native PowerShell command runtime.");
            return cmdlet.CommandRuntime;
        }

        private static MethodInfo RequireMethod(string name)
        {
            var method = RuntimeType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (method is null || method.ReturnType != typeof(void))
                throw new NotSupportedException("The loaded PowerShell host does not provide the generated function stream-variable contract: " + name + ".");
            return method;
        }

        private static void Invoke(MethodInfo method, object runtime)
        {
            try { method.Invoke(runtime, null); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }
}
