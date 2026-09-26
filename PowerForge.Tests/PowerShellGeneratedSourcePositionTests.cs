using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellGeneratedSourcePositionTests
{
    [Fact]
    public void ChunkedSourceCoordinatesPreserveAppendAndReplacementPositions()
    {
        var builder = new StringBuilder();
        for (var index = 0; index < 1000; index++) builder.Append("source text\r\n");
        builder.Append("ab\r");
        var position = PowerShellGeneratedSourcePosition.Get(builder);
        Assert.Equal((1001, 3), (position.Line, position.Column));
        builder.Append("\nc");
        position = PowerShellGeneratedSourcePosition.Get(builder);
        Assert.Equal((1002, 2), (position.Line, position.Column));
        builder.Clear().Append("replacement");
        position = PowerShellGeneratedSourcePosition.Get(builder);
        Assert.Equal((1, 12), (position.Line, position.Column));
    }
}
