using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Tracks the first non-zero native process exit code observed through <c>$LASTEXITCODE</c>.
/// </summary>
public sealed class PowerShellNativeExitCodeTracker : IDisposable
{
    private readonly SessionState sessionState;
    private readonly PSVariable? previousVariable;
    private readonly object? previousValue;
    private bool disposed;

    private PowerShellNativeExitCodeTracker(SessionState sessionState, PSVariable? previousVariable)
    {
        this.sessionState = sessionState;
        this.previousVariable = previousVariable;
        previousValue = previousVariable?.Value;
        Variable = new TrackingVariable(previousValue ?? 0);
    }

    /// <summary>Tracking variable installed for the active invocation.</summary>
    private TrackingVariable Variable { get; }

    /// <summary>First non-zero native exit code observed, if any.</summary>
    public int? FirstFailureExitCode => Variable.FirstFailureExitCode;

    /// <summary>Installs a tracking <c>LASTEXITCODE</c> variable in the current session state.</summary>
    /// <param name="sessionState">PowerShell session state.</param>
    /// <returns>Disposable tracker that restores the previous variable when disposed.</returns>
    public static PowerShellNativeExitCodeTracker Install(SessionState sessionState)
    {
        if (sessionState is null) throw new ArgumentNullException(nameof(sessionState));
        var previous = sessionState.PSVariable.Get("LASTEXITCODE");
        var tracker = new PowerShellNativeExitCodeTracker(sessionState, previous);
        sessionState.PSVariable.Set(tracker.Variable);
        tracker.Variable.Value = 0;
        return tracker;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        try
        {
            if (previousVariable is null)
            {
                sessionState.PSVariable.Remove("LASTEXITCODE");
            }
            else
            {
                previousVariable.Value = previousValue;
                sessionState.PSVariable.Set(previousVariable);
            }
        }
        catch
        {
            // Best-effort restoration; benchmark failure handling should not be hidden by cleanup.
        }
    }

    private sealed class TrackingVariable : PSVariable
    {
        private object? value;

        internal TrackingVariable(object value)
            : base("LASTEXITCODE", value)
        {
            this.value = value;
        }

        internal int? FirstFailureExitCode { get; private set; }

        public override object? Value
        {
            get => value;
            set
            {
                this.value = value;
                if (FirstFailureExitCode.HasValue || value is null)
                    return;
                try
                {
                    var exitCode = LanguagePrimitives.ConvertTo<int>(value);
                    if (exitCode != 0)
                        FirstFailureExitCode = exitCode;
                }
                catch
                {
                    // Non-numeric assignments are not native process exit codes.
                }
            }
        }
    }
}

/// <summary>
/// Adds native exit-code checks after PowerShell statements that may execute commands.
/// </summary>
public static class PowerShellNativeExitCodeGuard
{
    private const string CheckScript = "; if ($null -ne $global:LASTEXITCODE -and $global:LASTEXITCODE -ne 0) { throw \"Native command exited with code $global:LASTEXITCODE.\" }";

    /// <summary>Adds checks to script text.</summary>
    /// <param name="script">PowerShell script text.</param>
    /// <returns>Script text with native exit-code checks.</returns>
    public static string AddChecks(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;
        if (script.Contains("Native command exited with code $global:LASTEXITCODE.", StringComparison.Ordinal))
            return script;

        var ast = Parser.ParseInput(script, out _, out var errors);
        if (errors.Length > 0)
            return script;

        var statements = ast.FindAll(
                node => node is StatementAst statement
                        && IsStatementContainer(statement.Parent)
                        && StatementNeedsNativeExitCheck(statement),
                searchNestedScriptBlocks: true)
            .Cast<StatementAst>()
            .OrderByDescending(statement => statement.Extent.EndOffset)
            .ToArray();
        if (statements.Length == 0)
            return script;

        var guarded = script;
        foreach (var statement in statements)
            guarded = guarded.Insert(statement.Extent.EndOffset, CheckScript);
        return guarded;
    }

    private static bool IsStatementContainer(Ast? parent)
        => parent is NamedBlockAst or StatementBlockAst;

    private static bool StatementNeedsNativeExitCheck(StatementAst statement)
    {
        if (statement.Find(node => node is CommandAst, searchNestedScriptBlocks: true) is not null)
            return true;

        return statement is AssignmentStatementAst assignment
               && assignment.Left.Find(
                   node => node is VariableExpressionAst variable
                           && IsLastExitCodeVariable(variable),
                   searchNestedScriptBlocks: true) is not null;
    }

    private static bool IsLastExitCodeVariable(VariableExpressionAst variable)
    {
        var userPath = variable.VariablePath.UserPath;
        if (string.Equals(userPath, "LASTEXITCODE", StringComparison.OrdinalIgnoreCase))
            return true;

        var separator = userPath.LastIndexOf(':');
        return separator >= 0
               && string.Equals(userPath.Substring(separator + 1), "LASTEXITCODE", StringComparison.OrdinalIgnoreCase);
    }
}
