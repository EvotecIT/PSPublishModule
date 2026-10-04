namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly Lazy<MethodInfo> BinaryJoin = new(() => FindJoinOperator("JoinOperator"));
        private static readonly Lazy<MethodInfo> UnaryJoin = new(() => FindJoinOperator("UnaryJoinOperator"));

        /// <summary>Joins already evaluated operands with the active invocation's native conversion and enumeration rules.</summary>
        public string Join(object? values, object? separator, bool unary, string file,
            int line, int column, int endLine, int endColumn, string sourceText)
        {
            EnsureActive();
            var extent = PowerShellSourceExtent.Create(file, line, column, endLine, endColumn, sourceText);
            return (string)PowerShellNativeFunctionHost.Invoke(unary ? UnaryJoin.Value : BinaryJoin.Value, null,
                unary ? new object[] { _executionContext, extent, values! }
                      : new object[] { _executionContext, extent, values!, separator! })!;
        }

        private static MethodInfo FindJoinOperator(string name)
            => typeof(PSObject).Assembly.GetType("System.Management.Automation.ParserOps", true)!
                .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new NotSupportedException("PowerShell's native join operator is unavailable.");
    }
}
