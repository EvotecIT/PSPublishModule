namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        /// <summary>Transfers exit conversion and termination to the active PowerShell script owner.</summary>
        public void Exit(object? value)
        {
            EnsureActive();
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation.PipelineOps", true)!;
            var factory = type.GetMethod("GetExitException", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(object) }, null)
                ?? throw new NotSupportedException("PowerShell's native exit conversion is unavailable.");
            throw (FlowControlException)PowerShellNativeFunctionHost.Invoke(factory, null, new object[] { value! })!;
        }

        /// <summary>Leaves the current function through the active SDK's caller-loop transfer.</summary>
        public void TransferToCaller(bool isBreak)
        {
            EnsureActive();
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation." +
                (isBreak ? "BreakException" : "ContinueException"), true)!;
            throw (FlowControlException)Activator.CreateInstance(type, nonPublic: true)!;
        }

        /// <summary>Lets the nearest matching authored loop consume SDK callback break or continue.</summary>
        public bool IsLoopTransfer(FlowControlException error, bool isBreak, string label)
        {
            EnsureActive();
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation." +
                (isBreak ? "BreakException" : "ContinueException"), true)!;
            if (!type.IsInstanceOfType(error)) return false;
            var matches = type.GetMethod("MatchLabel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null)!;
            return (bool)PowerShellNativeFunctionHost.Invoke(matches, error, new object[] { label })!;
        }
    }
}
