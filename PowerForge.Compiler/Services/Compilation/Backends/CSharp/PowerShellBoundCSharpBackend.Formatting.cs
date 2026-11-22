using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private (string Name, string Format, string Value)? _formatHelper;

    private string EmitSafeScalarFormat(string format, string value)
    {
        _formatHelper ??= (_getTemporaryIdentifier("scalarFormat"),
            _getTemporaryIdentifier("formatTemplate"), _getTemporaryIdentifier("formatValue"));
        return $"{_formatHelper.Value.Name}(({format} ?? string.Empty), (object?)({value}))";
    }

    private void EmitFormatHelper(StringBuilder builder)
    {
        if (_formatHelper is not { } helper) return;
        // Read the culture after both authored operands have been evaluated.
        builder.Append("            static string ").Append(helper.Name).Append("(string ")
            .Append(helper.Format).Append(", object? ").Append(helper.Value)
            .Append(") => global::System.String.Format(global::System.Globalization.CultureInfo.CurrentCulture, ")
            .Append(helper.Format).Append(", ").Append(helper.Value).AppendLine(");");
    }
}
