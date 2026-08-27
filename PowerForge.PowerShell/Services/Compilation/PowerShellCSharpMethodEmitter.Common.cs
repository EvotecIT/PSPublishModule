using System.Globalization;
using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private static string EmitBracedPowerShellVariable(string name)
        => "${" + name.Replace("`", "``").Replace("}", "`}") + "}";

    private static bool HasAncestor<TAst>(Ast node) where TAst : Ast
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TAst) return true;
        }
        return false;
    }

    private static bool HasBreakableAncestor(Ast node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ForStatementAst or WhileStatementAst or ForEachStatementAst or SwitchStatementAst)
                return true;
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst)
                return false;
        }
        return false;
    }

    private static bool HasLoopAncestor(Ast node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ForStatementAst or WhileStatementAst or ForEachStatementAst)
                return true;
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst)
                return false;
        }
        return false;
    }

    private static bool HasContinuableAncestor(Ast node)
        => HasLoopAncestor(node) || HasAncestor<SwitchStatementAst>(node);

    private static bool CanAssign(Type target, Type source)
        => PowerShellClrTypeSemantics.CanAssign(target, source);

    internal static string SanitizeIdentifier(string value)
        => PowerShellClrSymbolMapper.MapIdentifier(value);

    internal static string GetTypeName(Type type)
    {
        if (type.IsArray) return GetTypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            var definitionName = (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0].Replace('+', '.');
            return $"global::{definitionName}<{string.Join(", ", type.GetGenericArguments().Select(GetTypeName))}>";
        }
        if (type == typeof(void)) return "void";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(char)) return "char";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";
        return "global::" + (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static string EmitConstant(ConstantExpressionAst constant)
        => constant.Value switch
        {
            null => "null",
            bool value => value ? "true" : "false",
            string value => EmitString(value),
            char value => EmitChar(value),
            float value => value.ToString("R", CultureInfo.InvariantCulture) + "F",
            double value => value.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal value => value.ToString(CultureInfo.InvariantCulture) + "M",
            long value => value.ToString(CultureInfo.InvariantCulture) + "L",
            ulong value => value.ToString(CultureInfo.InvariantCulture) + "UL",
            uint value => value.ToString(CultureInfo.InvariantCulture) + "U",
            System.Numerics.BigInteger value =>
                $"global::System.Numerics.BigInteger.Parse({EmitString(value.ToString(CultureInfo.InvariantCulture))}, global::System.Globalization.CultureInfo.InvariantCulture)",
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new PowerShellCSharpEmissionException(constant, $"Constant type '{constant.Value.GetType().FullName}' is not supported.")
        };

    private static string EmitString(string value) => PowerShellCSharpLiteral.QuoteString(value);
    private static string EmitChar(char value) => PowerShellCSharpLiteral.QuoteChar(value);
}
