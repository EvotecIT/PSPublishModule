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
        if (value.Tokens is { Count: > 0 })
            return FormatTokens(value.Tokens);

        switch ((value.Kind ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "null":
                return "$null";
            case "string":
                return FormatString(value.Text ?? string.Empty, preserveCharacterType: false);
            case "stringcodeunits":
                return FormatString(DecodeUtf16CodeUnits(value.Text), preserveCharacterType: false);
            case "char":
                return FormatString(value.Text ?? string.Empty, preserveCharacterType: true);
            case "charcodeunit":
                return FormatCharacterCodeUnit(value.Text);
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
            case "decimal":
                return "[System.Decimal]::Parse('" + (value.Text ?? string.Empty).Replace("'", "''") +
                       "', [System.Globalization.CultureInfo]::InvariantCulture)";
            case "scriptblockcodeunits":
                return "[scriptblock]::Create(" + FormatString(DecodeUtf16CodeUnits(value.Text), preserveCharacterType: false) + ")";
            case "collection":
                return "@(" + string.Join(", ", (value.Items ?? new List<DocumentationRuntimeValue>()).Select(Format)) + ")";
            case "formattable":
            case "text":
                return value.Text ?? string.Empty;
            case "textcodeunits":
                return DecodeUtf16CodeUnits(value.Text);
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
        var numericValue = value.Text ?? string.Empty;
        var underlyingTypeName = (value.UnderlyingTypeName ?? string.Empty).Trim();
        if (underlyingTypeName.Length > 0)
            numericValue = "([" + underlyingTypeName + "]" + numericValue + ")";
        return "[System.Enum]::ToObject([" + typeName + "], " + numericValue + ")";
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
        if (powerShellType.Equals("single", StringComparison.Ordinal))
        {
            if (value.Equals("-0", StringComparison.Ordinal)) value = "-0.0";
            else if (value.Equals("0", StringComparison.Ordinal)) value = "0.0";
            return "([single]" + value + ")";
        }
        if (value.Equals("-0", StringComparison.Ordinal)) return "-0.0";
        if (value.Equals("0", StringComparison.Ordinal)) return "0.0";
        return value;
    }

    private static string FormatString(string text, bool preserveCharacterType)
    {
        if (preserveCharacterType)
        {
            var character = text.Length > 0 ? text[0] : '\0';
            return "([char]" + ((int)character).ToString(CultureInfo.InvariantCulture) + ")";
        }

        if (!NeedsEncoding(text))
            return "'" + text.Replace("'", "''") + "'";

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

    private static string FormatTokens(IReadOnlyList<DocumentationRuntimeValue> tokens)
    {
        var collections = new Stack<List<string>>();
        string? result = null;

        foreach (var token in tokens)
        {
            var kind = (token.Kind ?? string.Empty).Trim();
            if (kind.Equals("CollectionStart", StringComparison.OrdinalIgnoreCase))
            {
                collections.Push(new List<string>());
                continue;
            }

            if (kind.Equals("CollectionEnd", StringComparison.OrdinalIgnoreCase))
            {
                if (collections.Count == 0)
                    throw new FormatException("The runtime default token stream contains an unexpected collection terminator.");
                Append("@(" + string.Join(", ", collections.Pop()) + ")");
                continue;
            }

            Append(Format(token));
        }

        if (collections.Count > 0)
            throw new FormatException("The runtime default token stream is missing a collection terminator.");
        return result ?? throw new FormatException("The runtime default token stream is empty.");

        void Append(string value)
        {
            if (collections.Count > 0)
            {
                collections.Peek().Add(value);
                return;
            }

            if (result is not null)
                throw new FormatException("The runtime default token stream contains trailing values.");
            result = value;
        }
    }

    private static string FormatCharacterCodeUnit(string? text)
    {
        if (!ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("The runtime default character code unit is invalid.");
        return "([char]" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    internal static string DecodeUtf16CodeUnits(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var values = text!.Split(',');
        var characters = new char[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            if (!ushort.TryParse(values[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new FormatException("The runtime default UTF-16 code-unit sequence is invalid.");
            characters[index] = (char)value;
        }
        return new string(characters);
    }
}
