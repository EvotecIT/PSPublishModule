using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    /// <summary>Finds the bounded invocation-owned receiver of a statement-output destination.</summary>
    internal static VariableExpressionAst? NativeCaptureAccessReceiver(ExpressionAst target)
    {
        while (target is AttributedExpressionAst attributed)
        {
            if (attributed.Attribute is not TypeConstraintAst) return null;
            target = attributed.Child;
        }
        return NativeAccessMutationReceiver(target);
    }

    private static bool CaptureAccessTypesAreAvailable(ExpressionAst target, string? framework,
        PowerShellCompilationCapability capabilities)
    {
        while (target is AttributedExpressionAst attributed)
        {
            if (attributed.Attribute is not TypeConstraintAst constraint ||
                constraint.TypeName.GetReflectionType() is not { } type ||
                !PowerShellCompilationParameterTypePolicy.CanUseInMethod(type, framework, capabilities))
                return false;
            target = attributed.Child;
        }
        return true;
    }
}
