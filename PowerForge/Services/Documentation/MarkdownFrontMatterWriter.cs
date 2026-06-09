using System.Text;

namespace PowerForge;

internal static class MarkdownFrontMatterWriter
{
    public static void Append(StringBuilder builder, params (string Key, string Value)[] entries)
    {
        builder.AppendLine("---");
        var yaml = new YamlTextWriter(entries.Length * 24);
        foreach (var (key, value) in entries)
            yaml.WriteScalar(key, value);
        builder.Append(yaml.ToString());
        builder.AppendLine("---");
    }
}
