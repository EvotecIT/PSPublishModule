using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

namespace PowerForge;

/// <summary>
/// Formats tagged PowerShell runtime values as stable, XML-safe PowerShell expressions.
/// </summary>
internal static class PowerShellDefaultValueFormatter
{
    /// <summary>
    /// Formats a captured runtime value for Markdown and MAML default-value metadata.
    /// </summary>
    /// <param name="value">Tagged value captured in the target PowerShell host.</param>
    /// <returns>A stable PowerShell expression, or an empty string when no value was captured.</returns>
    public static string Format(DocumentationRuntimeValue? value)
    {
        if (value is null) return string.Empty;

        switch ((value.Kind ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "null":
                return "$null";
            case "string":
                return FormatString(value.Text ?? string.Empty, preserveCharacterType: false);
            case "char":
                return FormatString(value.Text ?? string.Empty, preserveCharacterType: true);
            case "boolean":
                return string.Equals(value.Text, "True", StringComparison.OrdinalIgnoreCase)
                    ? "$true"
                    : "$false";
            case "enum":
                return FormatEnum(value);
            case "type":
                return string.IsNullOrWhiteSpace(value.CanonicalTypeName)
                    ? string.Empty
                    : "[" + value.CanonicalTypeName!.Trim() + "]";
            case "double":
                return FormatFloatingPoint(value.Text, "double");
            case "single":
                return FormatFloatingPoint(value.Text, "single");
            case "collection":
                return "@(" + string.Join(", ", (value.Items ?? new List<DocumentationRuntimeValue>()).Select(Format)) + ")";
            case "formattable":
            case "text":
                return value.Text ?? string.Empty;
            default:
                return value.Text ?? string.Empty;
        }
    }

    /// <summary>
    /// Returns whether authored help text must be represented as an expression to
    /// remain safe in XML and fixed-fence Markdown metadata blocks.
    /// </summary>
    public static bool NeedsEncoding(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var character in text!)
        {
            if (character == '\r' || character == '\n' || !XmlConvert.IsXmlChar(character))
                return true;
        }
        return false;
    }

    private static string FormatEnum(DocumentationRuntimeValue value)
    {
        var typeName = (value.CanonicalTypeName ?? string.Empty).Trim();
        if (typeName.Length == 0) return value.Text ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(value.Name))
            return "[" + typeName + "]::" + value.Name!.Trim();
        return "[System.Enum]::ToObject([" + typeName + "], " + (value.Text ?? string.Empty) + ")";
    }

    private static string FormatFloatingPoint(string? text, string powerShellType)
    {
        var value = text ?? string.Empty;
        if (value.Equals("NaN", StringComparison.OrdinalIgnoreCase))
            return "[" + powerShellType + "]::NaN";
        if (value.Equals("Infinity", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("+Infinity", StringComparison.OrdinalIgnoreCase))
            return "[" + powerShellType + "]::PositiveInfinity";
        if (value.Equals("-Infinity", StringComparison.OrdinalIgnoreCase))
            return "[" + powerShellType + "]::NegativeInfinity";
        return value;
    }

    private static string FormatString(string text, bool preserveCharacterType)
    {
        if (!NeedsEncoding(text))
            return "'" + text.Replace("'", "''") + "'";

        if (preserveCharacterType)
        {
            var character = text.Length > 0 ? text[0] : '\0';
            return "([char]" + ((int)character).ToString(CultureInfo.InvariantCulture) + ")";
        }

        var parts = new List<string>();
        var segment = new List<char>();

        void FlushSegment()
        {
            if (segment.Count == 0) return;
            parts.Add("'" + new string(segment.ToArray()).Replace("'", "''") + "'");
            segment.Clear();
        }

        foreach (var character in text)
        {
            if (character != '\r' && character != '\n' && XmlConvert.IsXmlChar(character))
            {
                segment.Add(character);
                continue;
            }

            FlushSegment();
            parts.Add("([char]" + ((int)character).ToString(CultureInfo.InvariantCulture) + ")");
        }

        FlushSegment();
        return "(-join @(" + string.Join(", ", parts) + "))";
    }
}
