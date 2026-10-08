using System.Management.Automation;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

[Cmdlet("Get", "ParsedRecords")]
public sealed class StatementErrorCmdlet : PSCmdlet
{
    [Parameter] public string[] Values { get; set; } = Array.Empty<string>();

    protected override void ProcessRecord()
    {
        var context = new PowerShellStatementErrorContext(this, "Get-ParsedRecords");
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
            foreach (var text in Values)
            {
                int parsed = 99;
                try
                {
                    try { parsed = int.Parse(text); }
                    catch (Exception error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
                    {
                        throw context.WrapInvocation(error, "Parse", 1);
                    }
                }
                catch (RuntimeException error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
                {
                    context.Handle(error, "source.ps1", 6, 9, 6, 37, "        $parsed = [int]::Parse($text)");
                }
                WriteObject(parsed);
            }
        }
        catch (Exception error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
        {
            context.Handle(error, "source.ps1", 4, 5, 8, 6, "    foreach ($text in $Values) {\n    }");
        }
        WriteObject("after");
    }
}
