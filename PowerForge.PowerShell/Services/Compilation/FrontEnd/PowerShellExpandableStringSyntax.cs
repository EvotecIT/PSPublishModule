using System.Globalization;
using System.Management.Automation.Language;
using System.Reflection;
using System.Text;

namespace PowerForge;

/// <summary>Reads the parser's interpolation slots without confusing decoded literal text with authored expressions.</summary>
internal static class PowerShellExpandableStringSyntax
{
    private static readonly PropertyInfo? FormatExpression = typeof(ExpandableStringExpressionAst).GetProperty(
        "FormatExpression", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static bool TryRead(ExpandableStringExpressionAst syntax, out IReadOnlyList<Fragment> fragments)
    {
        fragments = Array.Empty<Fragment>();
        if (FormatExpression?.PropertyType != typeof(string) || FormatExpression.GetValue(syntax) is not string format)
            return false;
        var result = new List<Fragment>();
        var literal = new StringBuilder();
        var expressionIndex = 0;
        for (var cursor = 0; cursor < format.Length;)
        {
            var character = format[cursor];
            if (character is not ('{' or '}'))
            {
                literal.Append(character);
                cursor++;
                continue;
            }
            if (cursor + 1 < format.Length && format[cursor + 1] == character)
            {
                literal.Append(character);
                cursor += 2;
                continue;
            }
            var slot = "{" + expressionIndex.ToString(CultureInfo.InvariantCulture) + "}";
            if (character != '{' || expressionIndex >= syntax.NestedExpressions.Count || cursor + slot.Length > format.Length ||
                string.Compare(format, cursor, slot, 0, slot.Length, StringComparison.Ordinal) != 0)
                return false;
            if (literal.Length != 0)
            {
                result.Add(new Fragment(literal.ToString(), null));
                literal.Clear();
            }
            result.Add(new Fragment(null, expressionIndex++));
            cursor += slot.Length;
        }
        if (expressionIndex != syntax.NestedExpressions.Count) return false;
        if (literal.Length != 0 || result.Count == 0) result.Add(new Fragment(literal.ToString(), null));
        fragments = result;
        return true;
    }

    internal sealed class Fragment
    {
        internal Fragment(string? text, int? expressionIndex)
        {
            Text = text;
            ExpressionIndex = expressionIndex;
        }

        internal string? Text { get; }
        internal int? ExpressionIndex { get; }
    }
}
