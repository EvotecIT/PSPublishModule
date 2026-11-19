using System.Text;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ConsoleEncodingTests
{
    // The console's encodings outlive a command and belong to the console window, so a command must put them back.
    [Fact]
    public void Utf8ScopeRestoresTheConsoleEncodingsAndLeavesThemAloneWhenNotSwitching()
    {
        var input = Console.InputEncoding;
        var output = Console.OutputEncoding;
        try
        {
            Console.InputEncoding = Encoding.ASCII;
            Console.OutputEncoding = Encoding.ASCII;

            using (ConsoleEncoding.UseUtf8(switchEncoding: false))
                Assert.Equal(Encoding.ASCII.CodePage, Console.OutputEncoding.CodePage);

            using (ConsoleEncoding.UseUtf8(switchEncoding: true))
            {
                Assert.Equal(Encoding.UTF8.CodePage, Console.OutputEncoding.CodePage);
                Assert.Equal(Encoding.UTF8.CodePage, Console.InputEncoding.CodePage);
                Assert.Empty(Console.OutputEncoding.GetPreamble());
            }

            Assert.Equal(Encoding.ASCII.CodePage, Console.OutputEncoding.CodePage);
            Assert.Equal(Encoding.ASCII.CodePage, Console.InputEncoding.CodePage);
        }
        finally
        {
            Console.InputEncoding = input;
            Console.OutputEncoding = output;
        }
    }
}
