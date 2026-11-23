using System.Text;

namespace PowerForge;

/// <summary>Proves a bounded numeric composite format without relying on sample argument values.</summary>
internal static class PowerShellRuntimeFreeFormatPolicy
{
    internal static bool IsSafe(PowerShellBoundExpression format, PowerShellTypeFact argumentType)
    {
        if (!PowerShellClrTypeSemantics.IsNumeric(argumentType.ClrType) &&
            argumentType.Provenance != PowerShellTypeFactProvenance.Int32OrDouble) return false;
        var template = new StringBuilder();
        return AppendTemplate(format, template) && Validate(template.ToString(), argumentType.ClrType);
    }

    private static bool AppendTemplate(PowerShellBoundExpression expression, StringBuilder template)
    {
        if (expression.Type.ClrType != typeof(string)) return false;
        switch (expression)
        {
            case PowerShellBoundLiteralExpression literal when literal.Value is null or string:
                template.Append((string?)literal.Value);
                return true;
            case PowerShellBoundVariableExpression { IsBraceFreeString: true }:
                // A dynamic fragment is a barrier: it cannot complete a format item or
                // join two braces into an escape, even when its eventual value is empty.
                template.Append('\0');
                return true;
            case PowerShellBoundBinaryExpression { Operation: PowerShellBoundBinaryOperator.Add } addition:
                return AppendTemplate(addition.Left, template) && AppendTemplate(addition.Right, template);
            case PowerShellBoundConversionExpression conversion when conversion.Operand.Type.ClrType == typeof(string):
                return AppendTemplate(conversion.Operand, template);
            case PowerShellBoundInterpolatedStringExpression interpolated:
                foreach (var part in interpolated.Parts)
                {
                    template.Append(part.Text);
                    if (part.Expression is not null && !AppendTemplate(part.Expression, template)) return false;
                }
                return true;
            default:
                return false;
        }
    }

    private static bool Validate(string template, Type argumentType)
    {
        var cursor = 0;
        while (cursor < template.Length)
        {
            var character = template[cursor++];
            if (character is not ('{' or '}')) continue;
            if (cursor < template.Length && template[cursor] == character)
            {
                cursor++;
                continue;
            }
            if (character == '}' || cursor >= template.Length || template[cursor++] != '0') return false;
            SkipSpaces(template, ref cursor);
            if (cursor < template.Length && template[cursor] == ',')
            {
                cursor++;
                SkipSpaces(template, ref cursor);
                if (cursor < template.Length && template[cursor] == '-') cursor++;
                var start = cursor;
                var width = 0;
                while (cursor < template.Length && IsDigit(template[cursor]))
                {
                    if (cursor - start >= 4) return false;
                    width = width * 10 + template[cursor++] - '0';
                }
                if (cursor == start || width > 4096) return false;
                SkipSpaces(template, ref cursor);
            }
            if (cursor < template.Length && template[cursor] == ':')
            {
                var start = ++cursor;
                while (cursor < template.Length && template[cursor] != '}') cursor++;
                if (!IsSafeSpecifier(template.Substring(start, cursor - start), argumentType)) return false;
            }
            if (cursor >= template.Length || template[cursor++] != '}') return false;
            // Framework interprets an escaped closing brace adjacent to a specifier
            // differently from modern .NET. Keep this ambiguous shape outside the proof.
            if (cursor < template.Length && template[cursor] == '}') return false;
        }
        return true;
    }

    private static bool IsSafeSpecifier(string specifier, Type argumentType)
    {
        if (specifier.Length == 0) return true;
        if (specifier.Length > 3) return false;
        var code = char.ToUpperInvariant(specifier[0]);
        if (code is not ('C' or 'E' or 'F' or 'G' or 'N' or 'P') &&
            !(code is 'D' or 'X' && PowerShellClrTypeSemantics.IsIntegral(argumentType))) return false;
        for (var index = 1; index < specifier.Length; index++)
            if (!IsDigit(specifier[index])) return false;
        return true;
    }

    private static bool IsDigit(char value) => value is >= '0' and <= '9';

    private static void SkipSpaces(string template, ref int cursor)
    {
        while (cursor < template.Length && template[cursor] == ' ') cursor++;
    }
}
