namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Text;

    /// <summary>Provides the line-reading behavior used by compiled console executables.</summary>
    internal static class PowerShellPackagedConsoleInput
    {
        internal static string? ReadLine()
        {
            if (!Console.IsInputRedirected)
                return Console.ReadLine();

            var input = Console.In;
            var line = new StringBuilder();
            int next;
            while ((next = input.Read()) >= 0)
            {
                var character = (char)next;
                Console.Out.Write(character);
                if (character == '\r' && input.Peek() == '\n')
                {
                    input.Read();
                    Console.Out.Write('\n');
                }
                if (character is '\r' or '\n')
                    return line.ToString();
                if (character == '\b')
                    line.Remove(line.Length - 1, 1);
                else
                    line.Append(character);
            }
            return line.Length == 0 ? null : line.ToString();
        }
    }
}
