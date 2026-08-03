using System.Xml;

namespace PowerForge.Web;

internal static partial class WebVisualStoryAnimatedArtifactValidator
{
    private static bool HasAnimationValue(XmlReader reader)
    {
        var values = reader.GetAttribute("values");
        if (!string.IsNullOrWhiteSpace(values))
        {
            var entries = values
                .Split(';')
                .Select(static value => value.Trim())
                .Where(static value => value.Length > 0)
                .ToArray();
            return entries.Length > 1 && entries.Skip(1).Any(value => !SmilValuesEqual(entries[0], value));
        }

        var to = reader.GetAttribute("to");
        if (!string.IsNullOrWhiteSpace(to))
        {
            var from = reader.GetAttribute("from");
            return string.IsNullOrWhiteSpace(from) || !SmilValuesEqual(from, to);
        }

        var by = reader.GetAttribute("by");
        return !string.IsNullOrWhiteSpace(by) && !IsDemonstrablyZeroSmilValue(by);
    }

    private static bool SmilValuesEqual(string left, string right)
    {
        var normalizedLeft = left.Trim();
        var normalizedRight = right.Trim();
        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
            return true;

        return TryParseSmilNumberList(normalizedLeft, out var leftNumbers) &&
               TryParseSmilNumberList(normalizedRight, out var rightNumbers) &&
               leftNumbers.SequenceEqual(rightNumbers);
    }

    private static bool IsDemonstrablyZeroSmilValue(string value)
        => TryParseSmilNumberList(value.Trim(), out var numbers) &&
           numbers.Count > 0 &&
           numbers.All(static number => number == 0d);

    private static bool TryParseSmilNumberList(string value, out IReadOnlyList<double> numbers)
    {
        var parsed = new List<double>();
        var tokens = value.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (!double.TryParse(
                    token,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number) ||
                !double.IsFinite(number))
            {
                numbers = Array.Empty<double>();
                return false;
            }
            parsed.Add(number);
        }

        numbers = parsed;
        return parsed.Count > 0;
    }
}
