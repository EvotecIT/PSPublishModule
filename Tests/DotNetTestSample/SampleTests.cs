using Xunit;

public sealed class SampleTests
{
    [Fact]
    public void Runs_on_the_selected_runtime() => Assert.True(System.Environment.Version.Major >= 4);

    [Fact]
    public void Preserves_profile_environment() => Assert.Equal("enabled", System.Environment.GetEnvironmentVariable("POWERFORGE_TEST_SAMPLE"));
}
