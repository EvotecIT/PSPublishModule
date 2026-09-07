using System.Management.Automation;
using System.Reflection;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

public static class InvocationFailureProbe
{
    public static int Invoke(string kind)
    {
        switch (kind)
        {
            case "wrapped": throw new TargetInvocationException(new FormatException("invalid"));
            case "double-wrapped": throw new TargetInvocationException(new TargetInvocationException(new FormatException("invalid")));
            case "wrapped-method": throw new TargetInvocationException(new MethodException("authored method failure"));
            case "wrapped-depth": throw new TargetInvocationException(new ScriptCallDepthException("authored depth failure"));
            case "method": throw new MethodException("authored method failure");
            case "invocation": throw new MethodInvocationException("authored invocation failure");
            case "depth": throw new ScriptCallDepthException("authored depth failure");
            default: throw new FormatException("invalid");
        }
    }
}

[Cmdlet("Get", "FailureRecords")]
public sealed class InvocationFailureCmdlet : PSCmdlet
{
    [Parameter] public string Kind { get; set; } = "format";

    protected override void ProcessRecord()
    {
        var context = new PowerShellStatementErrorContext(this, "Get-FailureRecords");
        try
        {
            try
            {
                WriteObject("before");
                try
                {
                    try { WriteObject(InvocationFailureProbe.Invoke(Kind)); }
                    catch (Exception error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
                    {
                        throw context.WrapInvocation(error, "Invoke", 1);
                    }
                }
                catch (Exception error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
                {
                    context.Handle(error, "source.ps1", 4, 5, 4, 78,
                        "    [Generic.Compiler.StatementErrors.InvocationFailureProbe]::Invoke($Kind)");
                }
                WriteObject("after");
            }
            finally { context.Dispose(); }
        }
        catch (RuntimeException error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
        {
            throw context.LeaveCommand(error);
        }
    }
}
