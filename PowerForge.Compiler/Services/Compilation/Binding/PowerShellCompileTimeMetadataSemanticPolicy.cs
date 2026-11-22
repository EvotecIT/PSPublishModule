using System.Diagnostics.CodeAnalysis;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Owns metadata that is meaningful while analyzing source but has no runtime command contract.</summary>
internal static class PowerShellCompileTimeMetadataSemanticPolicy
{
    private static readonly HashSet<string> SuppressMessageNamedArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(SuppressMessageAttribute.Justification),
        nameof(SuppressMessageAttribute.MessageId),
        nameof(SuppressMessageAttribute.Scope),
        nameof(SuppressMessageAttribute.Target)
    };

    internal static bool IsSupported(AttributeAst attribute)
    {
        if (attribute.Parent is not ParamBlockAst ||
            attribute.TypeName.GetReflectionType() != typeof(SuppressMessageAttribute) ||
            attribute.PositionalArguments.Count != 2 ||
            attribute.PositionalArguments.Any(static argument => argument is not StringConstantExpressionAst) ||
            attribute.NamedArguments.Any(static argument =>
                !SuppressMessageNamedArguments.Contains(argument.ArgumentName) ||
                argument.Argument is not StringConstantExpressionAst))
            return false;

        return attribute.NamedArguments
            .Select(static argument => argument.ArgumentName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == attribute.NamedArguments.Count;
    }
}
