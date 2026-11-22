namespace PowerForge;

public sealed partial class PowerShellCompilationProjectManifestService
{
    internal static ProjectContext OpenForDiagnostics(string projectPath)
    {
        var stage = PowerShellCompilationDiagnosticStage.Input;
        var target = string.Empty;
        try
        {
            var fullPath = Path.GetFullPath(projectPath.Trim().Trim('"'));
            var manifest = LoadCore(fullPath, requireInputs: true,
                (current, name) => { stage = current; target = name; });
            return new ProjectContext(fullPath, manifest);
        }
        catch (Exception exception) { throw new InspectionFailure(stage, target, exception); }
    }

    internal sealed class InspectionFailure : Exception
    {
        internal InspectionFailure(PowerShellCompilationDiagnosticStage stage, string target, Exception inner)
            : base(inner.Message, inner) { Stage = stage; Target = target; }
        internal PowerShellCompilationDiagnosticStage Stage { get; }
        internal string Target { get; }
    }
}
