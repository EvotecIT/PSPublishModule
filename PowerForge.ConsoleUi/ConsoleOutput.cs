using System;
using System.Threading;
using ConsoleMonster;
using ConsoleMonster.Capabilities;
using ConsoleMonster.Layout;
using ConsoleMonster.Live;
using ConsoleMonster.Text;

namespace PowerForge.ConsoleUi;

/// <summary>
/// Where human-facing output goes: the process console for the CLI, the calling cmdlet's PowerShell host for
/// the module. <see cref="ConsoleOutput"/> uses the host installed for the current flow.
/// </summary>
internal interface IConsoleHost
{
    /// <summary>Writes a widget once.</summary>
    void Write(IWidget widget);

    /// <summary>Writes markup, ending the line when <paramref name="newLine"/> is true.</summary>
    void WriteMarkup(string markup, bool newLine);

    /// <summary>Runs host writes (warnings, prompts) clear of a live area that is drawing on the console.</summary>
    void WriteOutside(Action write);

    /// <summary>Whether a live view can draw on the console for this host.</summary>
    bool CanRenderLive { get; }
}

/// <summary>
/// The one route for ConsoleMonster output from the CLI and the module. Callers build widgets and hand them here;
/// this class owns the profile (including <see cref="ConsoleMode"/>) and the destination (<see cref="Use"/>).
/// </summary>
internal static class ConsoleOutput
{
    private static readonly AsyncLocal<IConsoleHost?> CurrentHost = new();

    /// <summary>The profile for a write happening now, after the console-mode policy.</summary>
    public static TerminalProfile CurrentProfile() => ConsoleMode.Apply(Terminal.GetCurrentProfile());

    /// <summary>The host for the current flow; the process console unless a cmdlet installed its host.</summary>
    internal static IConsoleHost Host => CurrentHost.Value ?? TerminalHost.Instance;

    /// <summary>
    /// Sends output in the current flow to <paramref name="host"/> and applies <c>POWERFORGE_CONSOLE_MODE</c> to every
    /// ConsoleMonster profile resolved in it, including live sessions and host writes. Dispose to restore.
    /// </summary>
    public static IDisposable Use(IConsoleHost host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        var previous = CurrentHost.Value;
        CurrentHost.Value = host;
        var policy = Terminal.UseProfilePolicy(ConsoleMode.Apply);
        return new Scope(() =>
        {
            policy.Dispose();
            CurrentHost.Value = previous;
        });
    }

    public static void Write(IWidget widget) => Host.Write(widget);

    public static void WriteMarkupLine(string markup) => Host.WriteMarkup(markup ?? string.Empty, newLine: true);

    public static void WriteLine(string? text = null) => Host.WriteMarkup(Markup.Escape(text ?? string.Empty), newLine: true);

    /// <summary>Runs host writes such as a PowerShell warning or prompt above any live area instead of under it.</summary>
    public static void WriteOutside(Action write)
    {
        if (write is null) throw new ArgumentNullException(nameof(write));
        Host.WriteOutside(write);
    }

    /// <summary>Whether a live view can draw on the console for the current host.</summary>
    public static bool CanRenderLive => Host.CanRenderLive;

    private sealed class Scope(Action dispose) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose();
        }
    }

    /// <summary>The process console, used by the CLI and by module code running off the pipeline thread.</summary>
    internal sealed class TerminalHost : IConsoleHost
    {
        internal static readonly TerminalHost Instance = new();

        public void Write(IWidget widget) => Terminal.Write(widget, CurrentProfile());

        public void WriteMarkup(string markup, bool newLine) => Terminal.WriteMarkup(markup, newLine, CurrentProfile(), writer: null);

        public void WriteOutside(Action write)
        {
            var session = LiveSession.Current;
            if (session is null) write();
            else session.WriteOutside(write);
        }

        public bool CanRenderLive
        {
            get
            {
                var profile = CurrentProfile();
                return profile.Mode == OutputMode.Interactive && Terminal.IsConsole(Terminal.Target);
            }
        }
    }
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
