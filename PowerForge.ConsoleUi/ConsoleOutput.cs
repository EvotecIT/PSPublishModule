using System;
using ConsoleMonster;
using ConsoleMonster.Capabilities;
using ConsoleMonster.Layout;
using ConsoleMonster.Text;

namespace PowerForge.ConsoleUi;

/// <summary>
/// The one route for ConsoleMonster output from the CLI and the module. Callers build widgets and hand
/// them here; this class owns the profile (including <see cref="ConsoleMode"/>) and the destination.
/// </summary>
internal static class ConsoleOutput
{
    /// <summary>The profile for a write happening now, after the console-mode policy.</summary>
    public static TerminalProfile CurrentProfile() => ConsoleMode.Apply(Terminal.GetCurrentProfile());

    public static void Write(IWidget widget) => Terminal.Write(widget, CurrentProfile());

    public static void WriteMarkupLine(string markup) => Terminal.WriteMarkupLine(markup, CurrentProfile(), writer: null);

    public static void WriteLine(string? text = null) => Terminal.WriteRich(RichText.Plain(text ?? string.Empty), newLine: true, CurrentProfile());
}

/// <summary>
/// Maps <c>POWERFORGE_CONSOLE_MODE</c> onto ConsoleMonster's glyph tier. Unset keeps ConsoleMonster's detection,
/// including its own <c>CONSOLEMONSTER_*</c> overrides.
/// </summary>
internal static class ConsoleMode
{
    public const string Variable = "POWERFORGE_CONSOLE_MODE";

    public static TerminalProfile Apply(TerminalProfile profile)
        => Apply(profile, Environment.GetEnvironmentVariable(Variable));

    internal static TerminalProfile Apply(TerminalProfile profile, string? mode)
    {
        switch (mode?.Trim().ToLowerInvariant())
        {
            case "ascii": return profile.WithGlyphs(GlyphTier.Ascii);
            case "unicode": return profile.WithGlyphs(GlyphTier.Basic);
            case "rich": return profile.WithGlyphs(GlyphTier.Full);
            default: return profile;
        }
    }
}
