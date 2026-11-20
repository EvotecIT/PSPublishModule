namespace PowerForge.Tests;

public sealed partial class RunnerMaintenanceTests
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int AccessDenied = unchecked((int)0x80070005);

    [Theory]
    [InlineData(SharingViolation)]
    [InlineData(LockViolation)]
    public void MaintenanceRetrySharing_WaitsOutABriefScannerHandle(int hresult)
    {
        var attempts = 0;
        var waits = new List<TimeSpan>();

        var value = RunnerHousekeepingService.MaintenanceRetrySharing(() =>
        {
            if (++attempts < 3) throw new IOException("held by a scanner", hresult);
            return "opened";
        }, stop: static () => false, wait: waits.Add);

        Assert.Equal("opened", value);
        Assert.Equal(3, attempts);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200) }, waits);
    }

    [Fact]
    public void MaintenanceRetrySharing_DoesNotRetryOtherFailures()
    {
        var attempts = 0;

        var error = Assert.Throws<IOException>(() => RunnerHousekeepingService.MaintenanceRetrySharing<int>(() =>
        {
            attempts++;
            throw new IOException("Recent temporary entry retained.", AccessDenied);
        }, stop: static () => false, wait: static _ => { }));

        Assert.Equal(1, attempts);
        Assert.Equal(AccessDenied, error.HResult);
    }

    [Fact]
    public void MaintenanceRetrySharing_GivesUpOnAPersistentHolderAndWhenTheRunExpires()
    {
        var persistent = 0;
        Assert.Throws<IOException>(() => RunnerHousekeepingService.MaintenanceRetrySharing<int>(() =>
        {
            persistent++;
            throw new IOException("held", SharingViolation);
        }, stop: static () => false, wait: static _ => { }));

        var expired = 0;
        Assert.Throws<IOException>(() => RunnerHousekeepingService.MaintenanceRetrySharing<int>(() =>
        {
            expired++;
            throw new IOException("held", SharingViolation);
        }, stop: static () => true, wait: static _ => { }));

        Assert.Equal(5, persistent);
        Assert.Equal(1, expired);
    }
}
