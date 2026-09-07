using System.Management.Automation;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

[Cmdlet("Get", "HandledRecords")]
public sealed class HandledErrorCmdlet : PSCmdlet
{
    [Parameter] public string Mode { get; set; } = "parse";

    protected override void ProcessRecord()
    {
        var context = new PowerShellStatementErrorContext(this, "Get-HandledRecords");
        try
        {
            try { Run(context); }
            finally { context.Dispose(); }
        }
        catch (RuntimeException error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
        {
            throw context.LeaveCommand(error);
        }
    }

    private void Run(PowerShellStatementErrorContext context)
    {
        WriteObject("before");
        try
        {
            try
            {
                using (context.EnterHandler())
                {
                    try
                    {
                        if (Mode == "throw")
                            throw context.PrepareThrow(new InvalidOperationException("authored"), "source.ps1", 6, 9, 6, 80, "throw [InvalidOperationException]::new('authored')");
                        try { WriteObject(int.Parse("bad")); }
                        catch (Exception error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
                        {
                            throw context.WrapInvocation(error, "Parse", 1);
                        }
                    }
                    catch (Exception error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
                    {
                        context.Handle(error, "source.ps1", 7, 9, 7, 28, "[int]::Parse('bad')");
                    }
                    WriteObject("inside-after");
                }
            }
            catch (Exception error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
            {
                if (Mode == "finally") throw;
                var clause = context.FindCatch(error,
                    new Type?[] { typeof(FormatException), typeof(InvalidOperationException), null },
                    new[] { 0, 1, 2 }, out var caughtRecord);
                if (clause < 0) throw;
                WriteObject(clause == 0 ? "format" : clause == 1 ? "invalid" : "other");
                WriteObject(caughtRecord!.FullyQualifiedErrorId + ":" + caughtRecord.Exception.GetType().FullName);
            }
            finally { WriteObject("cleanup"); }
        }
        catch (Exception error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
        {
            context.Handle(error, "source.ps1", 4, 5, 15, 6, "try {\n}");
        }
        WriteObject("after");
    }
}
