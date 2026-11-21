namespace PowerForge.Generated.Runtime;

internal static class PowerShellStrictConsoleProgress
{
    private static int _columns;

    internal static void Write(string activity, bool completed)
    {
        if (Console.IsOutputRedirected) return;
        if (_columns != 0)
            Console.Out.Write("\r" + new string(' ', _columns) + "\r");
        if (completed)
        {
            _columns = 0;
            return;
        }
        Console.Out.Write(activity);
        _columns = activity.Length;
    }
}
