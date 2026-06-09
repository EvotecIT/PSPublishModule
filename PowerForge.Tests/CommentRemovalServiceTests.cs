using PowerForge;

namespace PowerForge.Tests;

public sealed class CommentRemovalServiceTests
{
    [Fact]
    public void Process_preserves_comments_before_param_block_by_default()
    {
        var service = new CommentRemovalService();
        var result = service.Process(new CommentRemovalRequest
        {
            Content = """
                function Test-Thing {
                    # kept before param
                    param(
                        [string] $Name
                    )
                    # removed body comment
                    "ok"
                }
                """
        });

        Assert.Contains("# kept before param", result, StringComparison.Ordinal);
        Assert.DoesNotContain("# removed body comment", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_removes_comments_before_param_block_when_requested()
    {
        var service = new CommentRemovalService();
        var result = service.Process(new CommentRemovalRequest
        {
            Content = """
                function Test-Thing {
                    # remove before param
                    param(
                        [string] $Name
                    )
                    "ok"
                }
                """,
            RemoveCommentsBeforeParamBlock = true
        });

        Assert.DoesNotContain("# remove before param", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_preserves_signature_block_when_requested()
    {
        var service = new CommentRemovalService();
        var result = service.Process(new CommentRemovalRequest
        {
            Content = """
                # normal comment
                Write-Host "Hello"
                # SIG # Begin signature block
                # sig payload
                # SIG # End signature block
                """,
            DoNotRemoveSignatureBlock = true
        });

        Assert.DoesNotContain("# normal comment", result, StringComparison.Ordinal);
        Assert.Contains("# SIG # Begin signature block", result, StringComparison.Ordinal);
        Assert.Contains("# sig payload", result, StringComparison.Ordinal);
        Assert.Contains("# SIG # End signature block", result, StringComparison.Ordinal);
    }
}
