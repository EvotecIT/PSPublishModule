using System.Globalization;
using System.Text;

namespace PowerForge;

/// <summary>Streams signing milestones without exposing certificate material or credentials.</summary>
internal static class ModuleSigningProgressProtocol
{
    internal const string UserGuidance = "Signing may wait for your certificate provider. A PIN, password, touch or consent dialog may take time to appear; check other windows. Timestamping also needs network access.";
    private const string Prefix = "PFSIGN::PROGRESS::";

    internal static bool TryParse(string line, out int completed, out int total, out string detail)
    {
        completed = total = 0;
        detail = string.Empty;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var fields = line.Substring(Prefix.Length).Split(new[] { ':' }, 3);
        if (fields.Length != 3 ||
            !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out completed) ||
            !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out total) ||
            completed < 0 || total < 0 || completed > total) return false;
        try
        {
            detail = Encoding.UTF8.GetString(Convert.FromBase64String(fields[2]));
            return !string.IsNullOrWhiteSpace(detail);
        }
        catch (FormatException) { return false; }
    }
}
