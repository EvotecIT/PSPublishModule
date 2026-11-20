using PowerForge.Compiler.App.Engine;

namespace PowerForge.Compiler.App.ViewModels;

/// <summary>One stop on the mode track: what Package, Hybrid or Strict would produce.</summary>
public sealed class ModeCardViewModel : ObservableObject
{
    private bool _isSelected;

    /// <summary>Creates a stop.</summary>
    public ModeCardViewModel(ModeCheck check, bool isRecommended)
    {
        Check = check;
        IsRecommended = isRecommended;
    }

    /// <summary>The engine's result for this mode.</summary>
    public ModeCheck Check { get; }

    /// <summary>The mode.</summary>
    public PowerShellCompilationMode Mode => Check.Mode;

    /// <summary>Mode name.</summary>
    public string Title => Mode.ToString();

    /// <summary>Short result under the mode name.</summary>
    public string OutcomeText => Check.Outcome switch
    {
        CompileOutcome.Native => "All compiled to .NET",
        CompileOutcome.Mixed => $"{Check.TypedUnits} of {Check.TypedUnits + Check.RuntimeUnits} compiled to .NET",
        CompileOutcome.Packaged => "Runs on PowerShell",
        _ => ErrorCount == 1 ? "1 error" : $"{ErrorCount} errors"
    };

    /// <summary>One sentence describing what this mode would build.</summary>
    public string Verdict => Check.Outcome switch
    {
        CompileOutcome.Native => $"{Title} compiles everything to .NET. The executable doesn't need PowerShell to run.",
        CompileOutcome.Mixed => $"{Title} compiles {Check.TypedUnits} of {Check.TypedUnits + Check.RuntimeUnits} parts to .NET. The rest runs on the PowerShell runtime inside the executable.",
        CompileOutcome.Packaged => $"{Title} packs the script with the PowerShell runtime. Nothing is compiled to .NET, but it runs anywhere PowerShell would.",
        _ => $"{Title} can't build this yet. {(ErrorCount == 1 ? "One error blocks it" : $"{ErrorCount} errors block it")}; the problems list shows where."
    };

    /// <summary>What the mode does, in plain words.</summary>
    public string Description => Mode switch
    {
        PowerShellCompilationMode.Strict => "Everything as .NET code",
        PowerShellCompilationMode.Hybrid => ".NET where it can, PowerShell for the rest",
        _ => "The script as it is, with PowerShell"
    };

    /// <summary>True for the engine's suggested mode.</summary>
    public bool IsRecommended { get; }

    /// <summary>True when this stop is the chosen mode.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>True when the mode cannot build this input.</summary>
    public bool IsBlocked => Check.Outcome == CompileOutcome.Blocked;

    /// <summary>True when everything compiles to typed code.</summary>
    public bool IsNative => Check.Outcome == CompileOutcome.Native;

    /// <summary>True when part of the input compiles to typed code.</summary>
    public bool IsMixed => Check.Outcome == CompileOutcome.Mixed;

    /// <summary>True when the input runs on the PowerShell runtime.</summary>
    public bool IsPackaged => Check.Outcome == CompileOutcome.Packaged;

    private int ErrorCount => Check.Diagnostics.Count(static diagnostic => diagnostic.Severity == CompileSeverity.Error);
}

/// <summary>A finding shown in the problems list.</summary>
public sealed class DiagnosticItemViewModel
{
    /// <summary>Creates an item.</summary>
    public DiagnosticItemViewModel(CompileDiagnostic diagnostic) => Diagnostic = diagnostic;

    /// <summary>The finding.</summary>
    public CompileDiagnostic Diagnostic { get; }

    /// <summary>Message text.</summary>
    public string Message => Diagnostic.Message;

    /// <summary>File and line.</summary>
    public string Location => Diagnostic.Location;

    /// <summary>True when the finding has a location.</summary>
    public bool HasLocation => !string.IsNullOrEmpty(Diagnostic.Location);

    /// <summary>Compiler code, for searching docs.</summary>
    public string Code => string.IsNullOrEmpty(Diagnostic.FeatureId) ? Diagnostic.Code : Diagnostic.FeatureId;

    /// <summary>Severity label.</summary>
    public string SeverityText => Diagnostic.Severity switch
    {
        CompileSeverity.Error => "Error",
        CompileSeverity.Warning => "Warning",
        _ => "Note"
    };

    /// <summary>True for errors.</summary>
    public bool IsError => Diagnostic.Severity == CompileSeverity.Error;

    /// <summary>True for warnings.</summary>
    public bool IsWarning => Diagnostic.Severity == CompileSeverity.Warning;

    /// <summary>True for notes.</summary>
    public bool IsNote => Diagnostic.Severity == CompileSeverity.Info;
}

/// <summary>One build output line.</summary>
public sealed class LogLineViewModel
{
    /// <summary>Creates a line.</summary>
    public LogLineViewModel(string text, CompileSeverity severity)
    {
        Text = text;
        Severity = severity;
    }

    /// <summary>Line text.</summary>
    public string Text { get; }

    /// <summary>Line severity.</summary>
    public CompileSeverity Severity { get; }

    /// <summary>True for error lines.</summary>
    public bool IsError => Severity == CompileSeverity.Error;

    /// <summary>True for warning lines.</summary>
    public bool IsWarning => Severity == CompileSeverity.Warning;
}
