namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
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
